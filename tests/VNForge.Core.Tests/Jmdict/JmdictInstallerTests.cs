using System.Net;
using VNForge.Core.Jmdict;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>O download do JMdict na primeira vez (#538): vai para uma pasta do usuário, nunca para o repositório, e uma falha não estraga o que já estava instalado.</summary>
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
    public async Task Baixa_o_arquivo_do_EDRDG_e_monta_o_indice_na_pasta_do_usuario()
    {
        var server = new FakeServer(_ => Ok(SyntheticJmdict.GzipBytes()));
        var store = Store;
        var etapas = new List<JmdictProgress>();

        await Installer(store, server).InstallAsync(new SyncProgress<JmdictProgress>(etapas.Add), TestContext.Current.CancellationToken);

        Assert.Equal(JmdictInstaller.SourceUri, Assert.Single(server.Requests));
        Assert.Equal("www.edrdg.org", JmdictInstaller.SourceUri.Host);
        Assert.True(store.IsInstalled);
        Assert.Equal(13, store.Open()!.EntryCount);
        Assert.Equal([store.IndexPath], Directory.GetFiles(store.Directory));
        Assert.Contains(etapas, p => p.Stage == JmdictStage.Downloading);
        Assert.Equal(new JmdictProgress(JmdictStage.Building, 1), etapas[^1]);
    }

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

    /// <summary>Um corpo de resposta que nunca entrega um byte: só termina quando a leitura é cancelada.</summary>
    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    /// <summary>Um corpo que entrega alguns bytes e perde a conexão.</summary>
    private sealed class DroppingStream : Stream
    {
        private bool first = true;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (first)
            {
                first = false;
                buffer.Span[0] = 0x1F;
                return ValueTask.FromResult(1);
            }

            throw new IOException("Foi forçado o cancelamento de uma conexão existente pelo host remoto.");
        }
    }

    [Fact]
    public async Task Download_que_para_de_receber_dados_estoura_o_prazo_com_mensagem_e_sem_arquivo()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new HangingStream()) });
        var store = Store;
        var installer = new JmdictInstaller(new HttpClient(server), store, TimeSpan.FromMilliseconds(150));

        var erro = await Assert.ThrowsAsync<JmdictFileException>(() => installer.InstallAsync(null, TestContext.Current.CancellationToken));

        Assert.Contains("sem receber dados", erro.Message);
        Assert.False(store.IsInstalled);
        Assert.Empty(Directory.GetFiles(store.Directory));
    }

    [Fact]
    public async Task Conexao_que_cai_no_meio_do_corpo_e_erro_de_rede_e_nao_de_gravacao_em_disco()
    {
        var server = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DroppingStream()) });
        var store = Store;

        var erro = await Assert.ThrowsAsync<JmdictFileException>(() => Installer(store, server).InstallAsync(null, TestContext.Current.CancellationToken));

        Assert.Contains("conexão", erro.Message);
        Assert.DoesNotContain("gravar", erro.Message);
        Assert.Empty(Directory.GetFiles(store.Directory));
    }

    [Fact]
    public async Task Cancelar_na_etapa_de_montagem_nao_troca_o_indice_nem_deixa_arquivo()
    {
        var store = Store;
        await Installer(store, new FakeServer(_ => Ok(SyntheticJmdict.GzipBytes()))).InstallAsync(null, TestContext.Current.CancellationToken);
        var antes = File.ReadAllBytes(store.IndexPath);
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress<JmdictProgress>(p =>
        {
            if (p.Stage == JmdictStage.Building && p.Fraction == 0)
            {
                cancel.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Installer(store, new FakeServer(_ => Ok(SyntheticJmdict.GzipBytes()))).InstallAsync(progress, cancel.Token));

        Assert.Equal(antes, File.ReadAllBytes(store.IndexPath));
        Assert.Equal([store.IndexPath], Directory.GetFiles(store.Directory));
    }

    [Fact]
    public async Task O_dicionario_com_mais_de_30_dias_pede_atualizacao_como_a_licenca_exige()
    {
        var store = Store;
        await Installer(store, new FakeServer(_ => Ok(SyntheticJmdict.GzipBytes()))).InstallAsync(null, TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(store.IndexPath, Agora.UtcDateTime.AddDays(-29));
        Assert.False(store.IsStale(Agora));

        File.SetLastWriteTimeUtc(store.IndexPath, Agora.UtcDateTime.AddDays(-31));

        Assert.True(store.IsStale(Agora));
        Assert.Equal(Agora.UtcDateTime.AddDays(-31), store.InstalledAt!.Value.UtcDateTime);
    }
}
