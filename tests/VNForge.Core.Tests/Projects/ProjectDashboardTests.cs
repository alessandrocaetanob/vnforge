using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectDashboardTests
{
    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Linha(string key, int n, string? falante, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    private static ProjectFolder ProjetoComTextos(string raiz)
    {
        var projeto = NovoProjeto(raiz);
        projeto.SaveText("yst00001",
        [
            Linha("yst00001", 1, "美咲", TranslationStatus.Untranslated),
            Linha("yst00001", 2, "美咲", TranslationStatus.Machine),
            Linha("yst00001", 3, null, TranslationStatus.Approved),
            Linha("yst00001", 4, "健", TranslationStatus.Translated),
        ]);
        projeto.SaveText("yst00002", [Linha("yst00002", 1, "健", TranslationStatus.Reviewed), Linha("yst00002", 2, "美咲", TranslationStatus.Machine)]);
        return projeto;
    }

    [Fact]
    public void O_progresso_e_o_mesmo_que_o_vnf_status_calcula()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);

        var painel = ProjectDashboard.Read(projeto);
        var status = ProjectStatusReporter.Create(projeto);

        Assert.Equal(status.Total, painel.Status.Total);
        Assert.Equal(status.Files, painel.Status.Files);
        Assert.Equal(status.Characters, painel.Status.Characters);
    }

    [Fact]
    public void Etapas_de_traducao_e_conferencia_contam_as_pendencias_por_status()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);

        var painel = ProjectDashboard.Read(projeto);

        var traduzir = painel.Stages.Single(s => s.Key == PipelineStageKey.Translate);
        Assert.Equal(1, traduzir.Pending);
        Assert.False(traduzir.IsComplete);
        var conferir = painel.Stages.Single(s => s.Key == PipelineStageKey.Review);
        Assert.Equal(3, conferir.Pending);
        Assert.False(conferir.IsComplete);
    }

    [Fact]
    public void Tudo_revisado_ou_aprovado_completa_traduzir_e_conferir()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("yst00001", [Linha("yst00001", 1, null, TranslationStatus.Reviewed), Linha("yst00001", 2, null, TranslationStatus.Approved)]);

        var painel = ProjectDashboard.Read(projeto);

        Assert.True(painel.Stages.Single(s => s.Key == PipelineStageKey.Translate).IsComplete);
        var conferir = painel.Stages.Single(s => s.Key == PipelineStageKey.Review);
        Assert.True(conferir.IsComplete);
        Assert.Equal(0, conferir.Pending);
    }

    [Fact]
    public void Projeto_sem_nada_extraido_tem_todas_as_etapas_incompletas()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        var painel = ProjectDashboard.Read(projeto);

        Assert.Equal(
            [PipelineStageKey.Extract, PipelineStageKey.Translate, PipelineStageKey.Review, PipelineStageKey.Build, PipelineStageKey.Inject],
            painel.Stages.Select(s => s.Key));
        Assert.All(painel.Stages, s => Assert.False(s.IsComplete));
        Assert.Equal(AiUsageTotals.Empty, painel.AiUsage);
    }

    [Fact]
    public void Extracao_gravada_no_state_json_completa_a_etapa_Extrair()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveState(new PipelineState([], new Dictionary<string, StageState> { [ProjectExtractor.ExtractStage] = new(StageStatus.Completed) }));

        var extrair = ProjectDashboard.Read(projeto).Stages.Single(s => s.Key == PipelineStageKey.Extract);

        Assert.True(extrair.IsComplete);
        Assert.Equal(0, extrair.Pending);
    }

    [Fact]
    public void Pasta_de_backup_sem_manifesto_nao_completa_a_etapa_Injetar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.CreateDirectory(Path.Combine(projeto.BackupDirectory, "2026-10-24_1200"));

        Assert.False(ProjectDashboard.Read(projeto).Stages.Single(s => s.Key == PipelineStageKey.Inject).IsComplete);
    }

    [Fact]
    public void O_gasto_de_IA_vem_do_registro_do_projeto()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        AiUsageLog.TryRecord(projeto, new AiUsageTotals(1000, 500, 200, 100, 1.25m, 1));
        AiUsageLog.TryRecord(projeto, new AiUsageTotals(10, 5, 0, 0, 0.5m, 1));

        var gasto = ProjectDashboard.Read(projeto).AiUsage;

        Assert.Equal(new AiUsageTotals(1010, 505, 200, 100, 1.75m, 2), gasto);
        Assert.Equal(1815, gasto.TotalTokens);
    }
}
