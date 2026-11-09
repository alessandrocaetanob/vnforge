using System.Net.Http.Headers;

namespace VNForge.Core.Jmdict;

public enum JmdictStage
{
    Downloading,
    Building,
}

/// <summary>Andamento do download: a etapa e a fração dela (de 0 a 1; -1 quando o servidor não informa o tamanho).</summary>
public readonly record struct JmdictProgress(JmdictStage Stage, double Fraction);

/// <summary>
/// Baixa o <c>JMdict_e.gz</c> do EDRDG e monta o índice local. Roda só quando a pessoa pede (a primeira consulta oferece o download, com o tamanho e a origem à vista).
/// Tudo acontece em arquivos temporários da pasta do dicionário e o índice só troca de lugar no fim: uma falha, um cancelamento ou um arquivo que não é o JMdict
/// deixam o dicionário que já estava instalado como estava. O download se cancela pelo <see cref="CancellationToken"/>; o <see cref="HttpClient.Timeout"/> não cobre
/// o corpo da resposta (a leitura é feita com <see cref="HttpCompletionOption.ResponseHeadersRead"/>), então <paramref name="idleTimeout"/> cancela o download que
/// fica sem receber nenhum byte por esse tempo (30 s por padrão).
/// </summary>
public sealed class JmdictInstaller(HttpClient http, JmdictStore store, TimeSpan? idleTimeout = null)
{
    /// <summary>O JMdict só com as glosas em inglês, no site do EDRDG (<c>JMdict_e.gz</c>, cerca de 10 MB).</summary>
    public static readonly Uri SourceUri = new("https://www.edrdg.org/pub/Nihongo/JMdict_e.gz");

    /// <summary>O tamanho do download para o aviso à pessoa.</summary>
    public const string ApproximateSize = "10 MB";

    /// <summary>Quanto tempo o download espera por dados antes de desistir.</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeSpan idle = idleTimeout ?? DefaultIdleTimeout;

    /// <exception cref="JmdictFileException">Falha de rede, de gravação, tempo esgotado ou arquivo que não é o JMdict.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> foi cancelado.</exception>
    public async Task InstallAsync(IProgress<JmdictProgress>? progress, CancellationToken cancellationToken)
    {
        var download = Path.Combine(store.Directory, "jmdict.download");
        var building = Path.Combine(store.Directory, "jmdict.building");
        try
        {
            Directory.CreateDirectory(store.Directory);
            await DownloadAsync(download, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new JmdictProgress(JmdictStage.Building, 0));
            await Task.Run(() => JmdictImporter.ImportGzipFile(download, building), cancellationToken).ConfigureAwait(false);

            // O cancelamento que chega com o índice já montado vale até aqui: depois da troca o dicionário novo está instalado e quem chama não pode mais desistir dele.
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(building, store.IndexPath, overwrite: true);
            progress?.Report(new JmdictProgress(JmdictStage.Building, 1));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw DiskError(e);
        }
        finally
        {
            DeleteQuietly(download);
            DeleteQuietly(building);
        }
    }

    private JmdictFileException DiskError(Exception e) =>
        new($"Não foi possível gravar o dicionário em '{store.Directory}': {e.Message}", e);

    private async Task DownloadAsync(string destination, IProgress<JmdictProgress>? progress, CancellationToken cancellationToken)
    {
        // Um token que vence por falta de dados: cada leitura que traz bytes o reinicia.
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stalled.CancelAfter(idle);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SourceUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VNForge", "1"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stalled.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new JmdictFileException($"O site do EDRDG respondeu {(int)response.StatusCode} ao baixar o JMdict. Tente de novo mais tarde.");
            }

            await CopyBodyAsync(response, destination, progress, stalled).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw NetworkError($"Não foi possível baixar o JMdict ({e.Message}). Confira a conexão e tente de novo.", e);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw NetworkError($"O download do JMdict ficou {(int)idle.TotalSeconds} s sem receber dados e foi interrompido. Confira a conexão e tente de novo.", e);
        }
    }

    private static JmdictFileException NetworkError(string message, Exception inner) => new(message, inner);

    private async Task CopyBodyAsync(HttpResponseMessage response, string destination, IProgress<JmdictProgress>? progress, CancellationTokenSource stalled)
    {
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(stalled.Token).ConfigureAwait(false);
        await using var file = OpenForWrite(destination);
        var buffer = new byte[81920];
        long done = 0;
        while (true)
        {
            var read = await ReadBodyAsync(source, buffer, stalled.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            stalled.CancelAfter(idle);
            await WriteChunkAsync(file, buffer.AsMemory(0, read), stalled.Token).ConfigureAwait(false);
            done += read;
            progress?.Report(new JmdictProgress(JmdictStage.Downloading, total is > 0 ? (double)done / total.Value : -1));
        }
    }

    private static async Task<int> ReadBodyAsync(Stream source, byte[] buffer, CancellationToken token)
    {
        try
        {
            return await source.ReadAsync(buffer, token).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            throw NetworkError($"A conexão com o site do EDRDG caiu no meio do download ({e.Message}). Tente de novo.", e);
        }
    }

    private FileStream OpenForWrite(string path)
    {
        try
        {
            return File.Create(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw DiskError(e);
        }
    }

    private async Task WriteChunkAsync(FileStream file, ReadOnlyMemory<byte> chunk, CancellationToken token)
    {
        try
        {
            await file.WriteAsync(chunk, token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw DiskError(e);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sobra de um arquivo temporário não atrapalha: o próximo download o sobrescreve.
        }
    }
}
