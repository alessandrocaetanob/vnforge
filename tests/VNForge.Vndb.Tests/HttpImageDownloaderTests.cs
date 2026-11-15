using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VNForge.Vndb.Tests;

/// <summary>O downloader HTTP das imagens do VNDB: só o transporte do <see cref="StubHandler"/>, nenhuma conexão de rede.</summary>
[Trait("Risco", "R7")]
public sealed class HttpImageDownloaderTests
{
    private static readonly Uri Url = new("https://t.vndb.org/ch/12/3412.jpg");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpImageDownloader Downloader(StubHandler handler, TimeSpan? timeout = null, long? maxBytes = null) =>
        new(new HttpClient(handler), timeout, maxBytes ?? HttpImageDownloader.DefaultMaxBytes);

    private static HttpResponseMessage Bytes(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task Corpo_acima_do_limite_vira_nulo_sem_ler_tudo()
    {
        var handler = new StubHandler((_, _) => Bytes(new byte[100]));

        Assert.Null(await Downloader(handler, maxBytes: 99).DownloadAsync(Url, Ct));
        Assert.Equal(new byte[100], await Downloader(handler, maxBytes: 100).DownloadAsync(Url, Ct));
    }

    [Fact]
    public async Task Endereco_fora_do_vndb_nao_e_pedido()
    {
        var handler = new StubHandler((_, _) => Bytes([1]));

        Assert.Null(await Downloader(handler).DownloadAsync(new Uri("https://evil.example/x.jpg"), Ct));
        Assert.Null(await Downloader(handler).DownloadAsync(new Uri("http://t.vndb.org/x.jpg"), Ct));
        Assert.Empty(handler.Requests);
    }
}
