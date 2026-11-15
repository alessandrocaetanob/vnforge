using System.Net;
using VNForge.Core.Jmdict;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>O download do JMdict na primeira vez (#538): vai para uma pasta do usuário, nunca para o repositório, e uma falha não estraga o que já estava instalado.</summary>
[Trait("Risco", "R7")]
public sealed class JmdictInstallerTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private JmdictStore Store => new(Path.Combine(temp.Path, "dicionario"));

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static JmdictInstaller Installer(JmdictStore store, FakeServer server) => new(new HttpClient(server), store);

    [Fact]
    public async Task Resposta_de_erro_do_servidor_vira_mensagem_em_portugues_e_nao_deixa_arquivo()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var store = Store;

        var erro = await Assert.ThrowsAsync<JmdictFileException>(() => Installer(store, server).InstallAsync(null, TestContext.Current.CancellationToken));

        Assert.Contains("503", erro.Message);
        Assert.False(store.IsInstalled);
        Assert.Empty(Directory.Exists(store.Directory) ? Directory.GetFiles(store.Directory) : []);
    }

    [Fact]
    public async Task Falha_de_rede_e_resposta_que_nao_e_gzip_nao_mexem_no_dicionario_instalado()
    {
        var store = Store;
        await Installer(store, new FakeServer(_ => Ok(SyntheticJmdict.GzipBytes()))).InstallAsync(null, TestContext.Current.CancellationToken);
        var antes = File.ReadAllBytes(store.IndexPath);

        var semRede = new FakeServer(_ => throw new HttpRequestException("sem conexão"));
        await Assert.ThrowsAsync<JmdictFileException>(() => Installer(store, semRede).InstallAsync(null, TestContext.Current.CancellationToken));
        var html = new FakeServer(_ => Ok("<html>Acesso negado</html>"u8.ToArray()));
        await Assert.ThrowsAsync<JmdictFileException>(() => Installer(store, html).InstallAsync(null, TestContext.Current.CancellationToken));

        Assert.Equal(antes, File.ReadAllBytes(store.IndexPath));
        Assert.Equal([store.IndexPath], Directory.GetFiles(store.Directory));
    }

    [Fact]
    public void Sem_o_arquivo_a_loja_diz_que_nao_ha_dicionario_e_um_arquivo_estragado_e_erro()
    {
        var store = Store;
        Assert.False(store.IsInstalled);
        Assert.Null(store.Open());
        Assert.Null(store.InstalledAt);
        Assert.False(store.IsStale(Agora));

        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.IndexPath, "lixo");

        Assert.Throws<JmdictFileException>(() => store.Open());
    }
}
