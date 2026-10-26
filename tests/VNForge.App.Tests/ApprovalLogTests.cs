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
    public async Task Aprovar_conta_na_memoria_e_grava_em_segundo_plano()
    {
        var log = new ProjectApprovalLog(raiz);

        log.Record("yst00001", "yst00001:000001", Noon);
        log.Record("yst00001", "yst00001:000002", Noon.AddMinutes(1));

        Assert.Equal(2, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        await log.Saving;
        Assert.Equal(["yst00001:000001", "yst00001:000002"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
    }

    [Fact]
    public async Task Muitas_aprovacoes_seguidas_terminam_com_todas_no_arquivo_porque_a_ultima_gravacao_vence()
    {
        var log = new ProjectApprovalLog(raiz);

        for (var i = 0; i < 60; i++)
        {
            log.Record("yst00001", $"yst00001:{i:D6}", Noon.AddSeconds(i));
        }

        await log.Saving;

        Assert.Equal(60, SessionsFile.Load(Projeto).Approvals.Count);
        Assert.Equal(60, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        Assert.Empty(Directory.GetFiles(Projeto.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public async Task O_historico_do_arquivo_entra_na_contagem_e_continua_depois_da_nova_aprovacao()
    {
        SessionsFile.Save(Projeto, SessionsFile.Empty.With(Approval("yst00001:000010", -5)));
        var log = new ProjectApprovalLog(raiz);

        log.Record("yst00001", "yst00001:000011", Noon);
        await log.Saving;

        Assert.Equal(2, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        Assert.Equal(["yst00001:000010", "yst00001:000011"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
    }

    [Fact]
    public async Task Remove_tira_a_aprovacao_da_contagem_e_do_arquivo()
    {
        var log = new ProjectApprovalLog(raiz);
        log.Record("yst00001", "yst00001:000001", Noon);
        log.Record("yst00001", "yst00001:000002", Noon);

        log.Remove("yst00001", "yst00001:000001");
        await log.Saving;

        Assert.Equal(1, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        Assert.Equal(["yst00001:000002"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
    }

    [Fact]
    public async Task Arquivo_que_nao_abre_guarda_o_historico_em_memoria_e_nao_e_sobrescrito()
    {
        SessionsFile.Save(Projeto, SessionsFile.Empty.With(Approval("yst00001:000010", -5)));
        var original = File.ReadAllText(SessionsFile.PathFor(Projeto));
        var log = new ProjectApprovalLog(raiz);

        FileStream? bloqueio = new(SessionsFile.PathFor(Projeto), FileMode.Open, FileAccess.Read, FileShare.None);
        try
        {
            log.Record("yst00001", "yst00001:000011", Noon);
            await log.Saving;

            Assert.Equal(1, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        }
        finally
        {
            bloqueio.Dispose();
            bloqueio = null;
        }

        Assert.Equal(original, File.ReadAllText(SessionsFile.PathFor(Projeto)));

        log.Record("yst00001", "yst00001:000012", Noon.AddMinutes(1));
        await log.Saving;

        Assert.Equal(["yst00001:000010", "yst00001:000011", "yst00001:000012"], SessionsFile.Load(Projeto).Approvals.Select(a => a.UnitId));
        Assert.Equal(3, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
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

    [Fact]
    public async Task Pasta_que_nao_e_projeto_conta_na_memoria_sem_gravar_nada()
    {
        var log = new ProjectApprovalLog(Path.Combine(pasta.Path, "nao-existe"));

        log.Record("yst00001", "yst00001:000001", Noon);
        await log.Saving;

        Assert.Equal(1, log.ReviewedOn(Noon, TimeZoneInfo.Utc));
        Assert.False(Directory.Exists(Path.Combine(pasta.Path, "nao-existe")));
    }
}
