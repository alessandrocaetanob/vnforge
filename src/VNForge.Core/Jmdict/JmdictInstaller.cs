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
/// deixam o dicionário que já estava instalado como estava.
/// </summary>
public sealed class JmdictInstaller(HttpClient http, JmdictStore store)
{
    /// <summary>O JMdict só com as glosas em inglês, no site do EDRDG (<c>JMdict_e.gz</c>, cerca de 10 MB).</summary>
    public static readonly Uri SourceUri = new("https://www.edrdg.org/pub/Nihongo/JMdict_e.gz");

    /// <summary>O tamanho do download para o aviso à pessoa.</summary>
    public const string ApproximateSize = "10 MB";

    public async Task InstallAsync(IProgress<JmdictProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(store.Directory);
        var download = Path.Combine(store.Directory, "jmdict.download");
        var building = Path.Combine(store.Directory, "jmdict.building");
        try
        {
            await DownloadAsync(download, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new JmdictProgress(JmdictStage.Building, 0));
            await Task.Run(() => JmdictImporter.ImportGzipFile(download, building), cancellationToken).ConfigureAwait(false);
            File.Move(building, store.IndexPath, overwrite: true);
            progress?.Report(new JmdictProgress(JmdictStage.Building, 1));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new JmdictFileException($"Não foi possível gravar o dicionário em '{store.Directory}': {e.Message}", e);
        }
        finally
        {
            DeleteQuietly(download);
            DeleteQuietly(building);
        }
    }

    private async Task DownloadAsync(string destination, IProgress<JmdictProgress>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SourceUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VNForge", "1"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new JmdictFileException($"O site do EDRDG respondeu {(int)response.StatusCode} ao baixar o JMdict. Tente de novo mais tarde.");
            }

            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(destination);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress?.Report(new JmdictProgress(JmdictStage.Downloading, total is > 0 ? (double)done / total.Value : -1));
            }
        }
        catch (HttpRequestException e)
        {
            throw new JmdictFileException($"Não foi possível baixar o JMdict ({e.Message}). Confira a conexão e tente de novo.", e);
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
