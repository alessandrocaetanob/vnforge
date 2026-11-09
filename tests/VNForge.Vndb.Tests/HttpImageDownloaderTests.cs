using System.Net;

namespace VNForge.Vndb.Tests;

/// <summary>O downloader HTTP das imagens do VNDB: só o transporte do <see cref="StubHandler"/>, nenhuma conexão de rede.</summary>
public sealed class HttpImageDownloaderTests
{
    private static readonly Uri Url = new("https://t.vndb.org/ch/12/3412.jpg");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpImageDownloader Downloader(StubHandler handler, TimeSpan? timeout = null, long? maxBytes = null) =>
        new(new HttpClient(handler), timeout, maxBytes ?? HttpImageDownloader.DefaultMaxBytes);

    private static HttpResponseMessage Bytes(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task Devolve_os_bytes_e_se_identifica()
    {
        var handler = new StubHandler((_, _) => Bytes([1, 2, 3]));

        var bytes = await Downloader(handler).DownloadAsync(Url, Ct);

        Assert.Equal([1, 2, 3], bytes);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(Url, sent.Uri);
        Assert.StartsWith("VNForge", sent.UserAgent);
        Assert.Null(sent.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Resposta_de_erro_vira_nulo(HttpStatusCode status) =>
        Assert.Null(await Downloader(new StubHandler((_, _) => Bytes([1], status))).DownloadAsync(Url, Ct));

    [Fact]
    public async Task Rede_fora_vira_nulo() =>
        Assert.Null(await Downloader(new StubHandler((_, _) => throw new HttpRequestException("sem rede"))).DownloadAsync(Url, Ct));

    [Fact]
    public async Task Corpo_cortado_no_meio_vira_nulo()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StubHandler.BrokenContent() });

        Assert.Null(await Downloader(handler).DownloadAsync(Url, Ct));
    }

    [Fact]
    public async Task Corpo_acima_do_limite_vira_nulo_sem_ler_tudo()
    {
        var handler = new StubHandler((_, _) => Bytes(new byte[100]));

        Assert.Null(await Downloader(handler, maxBytes: 99).DownloadAsync(Url, Ct));
        Assert.Equal(new byte[100], await Downloader(handler, maxBytes: 100).DownloadAsync(Url, Ct));
    }

    /// <summary>Um corpo sem tamanho declarado, como uma resposta em pedaços: só a leitura descobre que passou do limite.</summary>
    private sealed class UnsizedContent(int size) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(new byte[size]);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    [Fact]
    public async Task Corpo_sem_tamanho_declarado_tambem_respeita_o_limite()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnsizedContent(100) });

        Assert.Null(await Downloader(handler, maxBytes: 99).DownloadAsync(Url, Ct));
        Assert.Equal(new byte[100], await Downloader(handler, maxBytes: 100).DownloadAsync(Url, Ct));
    }

    [Fact]
    public async Task Servidor_que_nao_responde_estoura_o_prazo_e_vira_nulo()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StubHandler.HangingContent() });

        Assert.Null(await Downloader(handler, TimeSpan.FromMilliseconds(50)).DownloadAsync(Url, Ct));
    }

    [Fact]
    public async Task Cancelar_lanca_e_nao_vira_nulo()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StubHandler.HangingContent() });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(handler, TimeSpan.FromSeconds(30)).DownloadAsync(Url, cts.Token));
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
