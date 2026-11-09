using VNForge.Core.Vndb;

namespace VNForge.Vndb;

/// <summary>
/// Baixa as imagens do VNDB (<c>t.vndb.org</c>) por HTTPS para o <see cref="VndbImageCache"/>. Erro de rede, resposta que não é 200, corpo acima do limite e prazo
/// estourado dão <c>null</c>: a tela fica com o marcador. Só cancelar lança. As imagens são arquivos estáticos e não passam pela fila de 200 pedidos da API.
/// </summary>
public sealed class HttpImageDownloader : IImageDownloader
{
    /// <summary>Quanto esperar por uma imagem inteira antes de desistir.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>O maior corpo que se aceita, o mesmo teto do <see cref="VndbImageCache"/>.</summary>
    public const long DefaultMaxBytes = VndbImageCache.MaxBytes;

    private static readonly Lazy<HttpClient> SharedHttp = new(CreateClient);

    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly long _maxBytes;

    public HttpImageDownloader(HttpClient http, TimeSpan? timeout = null, long maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        _http = http;
        _timeout = timeout ?? DefaultTimeout;
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// O <see cref="HttpClient"/> do <see cref="Create"/>. Sem redirecionamento: o endereço já foi conferido como de vndb.org, e um 302 poderia levar a outro lugar;
    /// a resposta 3xx não é de sucesso e vira <c>null</c>.
    /// </summary>
    internal static HttpClient CreateClient() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>O downloader do app e da CLI: um <see cref="HttpClient"/> só para o processo.</summary>
    public static HttpImageDownloader Create() => new(SharedHttp.Value);

    public async Task<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!VndbImageVisibility.IsAllowedSource(url.AbsoluteUri))
        {
            return null;
        }

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("VNForge");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > _maxBytes)
            {
                return null;
            }

            // Corpo sem Content-Length também respeita o teto: acima dele o buffer lança HttpRequestException, tratada abaixo.
            await response.Content.LoadIntoBufferAsync(_maxBytes, linked.Token).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return null;
        }
    }
}
