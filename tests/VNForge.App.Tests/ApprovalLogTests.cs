using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O registro de aprovações do app: na memória como fonte, gravação fora da thread da tela, e nunca por cima de um arquivo que não foi lido.</summary>
public sealed class ApprovalLogTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly TempFolder pasta = new();
    private readonly string raiz;

    public ApprovalLogTests()
    {
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Machine);
    }

    public void Dispose() => pasta.Dispose();

    private ProjectFolder Projeto => ProjectFolder.Open(raiz);

    private static ApprovalRecord Approval(string unit, int minutes) => new("yst00001", unit, Noon.AddMinutes(minutes));

    [Fact]
    public async Task Aprovacao_desfeita_com_o_arquivo_ilegivel_e_reaplicada_sobre_o_arquivo_relido()
    {
        SessionsFile.Save(Projeto, SessionsFile.Empty.With(Approval("yst00001:000010", -5)));
        var relogio = new FakeTimeProvider(Noon);
        var log = new ProjectApprovalLog(raiz, relogio);

        using (new FileStream(SessionsFile.PathFor(Projeto), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            log.Record("yst00001", "yst00001:000011", Noon);
            log.Remove("yst00001", "yst00001:000010");
            log.Remove("yst00001", "yst00001:000011");
            log.Record("yst00001", "yst00001:000012", Noon);
        }

        relogio.Advance(ProjectApprovalLog.RetryInterval);
        log.Record("yst00001", "yst00001:000013", Noon);
        await log.Saving;

        Assert.Equal(["yst00001:000012", "yst00001:000013"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
        Assert.Equal(2, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
    }

    [Fact]
    public async Task Pasta_que_ainda_nao_e_projeto_nao_marca_o_registro_como_lido()
    {
        var destino = Path.Combine(pasta.Path, "depois");
        var log = new ProjectApprovalLog(destino);
        log.Record("yst00001", "yst00001:000001", Noon);
        await log.Saving;

        SampleProjects.Create(destino, "Depois", TranslationStatus.Machine);
        SessionsFile.Save(ProjectFolder.Open(destino), SessionsFile.Empty.With(Approval("yst00001:000010", -5)));
        log.Record("yst00001", "yst00001:000002", Noon);
        await log.Saving;

        Assert.Equal(["yst00001:000010", "yst00001:000001", "yst00001:000002"], SessionsFile.Load(ProjectFolder.Open(destino)).Approvals.Select(a => a.UnitId));
        Assert.Equal(3, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
    }

    [Fact]
    public async Task FlushAll_espera_a_gravacao_pendente_e_diz_quando_ela_nao_terminou()
    {
        using var liberar = new ManualResetEventSlim();
        var log = ProjectApprovalLog.For(raiz, root => new ProjectApprovalLog(root, save: (pasta, registro) =>
        {
            liberar.Wait(TestContext.Current.CancellationToken);
            SessionsFile.Save(pasta, registro);
        }));
        log.Record("yst00001", "yst00001:000001", Noon);

        Assert.False(ProjectApprovalLog.FlushAll(TimeSpan.Zero));

        var espera = Task.Run(() => ProjectApprovalLog.FlushAll(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken);
        liberar.Set();

        Assert.True(await espera);
        Assert.Equal(["yst00001:000001"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
        Assert.True(log.Saving.IsCompleted);
    }

    [Fact]
    public async Task FlushAll_tenta_gravar_de_novo_o_que_ficou_pendente_depois_de_uma_falha()
    {
        var falhas = 1;
        var log = ProjectApprovalLog.For(raiz, root => new ProjectApprovalLog(root, save: (pasta, registro) =>
        {
            if (Interlocked.Exchange(ref falhas, 0) == 1)
            {
                throw new InvalidOperationException("falha de teste");
            }

            SessionsFile.Save(pasta, registro);
        }));
        log.Record("yst00001", "yst00001:000001", Noon);
        await log.Saving;
        Assert.Empty(SessionsFile.Load(Projeto).Approvals);

        ProjectApprovalLog.FlushAll(TimeSpan.FromSeconds(30));
        await log.Saving;

        Assert.Equal(["yst00001:000001"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
    }

    [Fact]
    public async Task Arquivo_de_versao_mais_nova_nunca_e_sobrescrito()
    {
        Directory.CreateDirectory(Projeto.CacheDirectory);
        const string Futuro = "{\"version\": 2, \"approvals\": [], \"sessions\": [{\"id\": 1}]}";
        File.WriteAllText(SessionsFile.PathFor(Projeto), Futuro);
        var log = new ProjectApprovalLog(raiz);

        log.Record("yst00001", "yst00001:000001", Noon);
        log.Record("yst00001", "yst00001:000002", Noon);
        await log.Saving;

        Assert.Equal(Futuro, File.ReadAllText(SessionsFile.PathFor(Projeto)));
        Assert.Equal(2, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
    }
}
