using System.Net;
using System.Net.Sockets;
using System.Text;

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
    public async Task Endereco_fora_do_vndb_nao_e_pedido()
    {
        var handler = new StubHandler((_, _) => Bytes([1]));

        Assert.Null(await Downloader(handler).DownloadAsync(new Uri("https://evil.example/x.jpg"), Ct));
        Assert.Null(await Downloader(handler).DownloadAsync(new Uri("http://t.vndb.org/x.jpg"), Ct));
        Assert.Empty(handler.Requests);
    }

    /// <summary>Um servidor só em 127.0.0.1 que responde 302 para o próprio endereço e conta quantos pedidos chegaram.</summary>
    private sealed class RedirectingServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _requests;

        public RedirectingServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        public int Port { get; }

        public int Requests => Volatile.Read(ref _requests);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task Serve()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];
                    _ = await stream.ReadAsync(buffer, _stop.Token);
                    Interlocked.Increment(ref _requests);
                    var reply = string.Join(string.Concat((char)13, (char)10), "HTTP/1.1 302 Found", $"Location: http://127.0.0.1:{Port}/segundo", "Content-Length: 0", "Connection: close", string.Empty, string.Empty);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), _stop.Token);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
            {
                // O teste terminou e fechou o servidor.
            }
        }
    }

    [Fact]
    public async Task O_cliente_do_Create_nao_segue_um_302_para_outro_endereco()
    {
        using var server = new RedirectingServer();
        using var client = HttpImageDownloader.CreateClient();

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/primeiro"), Ct);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, server.Requests);
    }
}
