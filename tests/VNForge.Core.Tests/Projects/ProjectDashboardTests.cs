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

    [Fact]
    public void A_conferencia_conta_erros_e_avisos_sem_as_linhas_sem_traducao()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("yst00001",
        [
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, "原文\\p", "texto  com espaço duplo\\p", TranslationStatus.Translated),
            new TextUnit("yst00001:000002", TextUnitKind.Dialogue, null, "原文\\p", "sem o marcador", TranslationStatus.Translated),
            new TextUnit("yst00001:000003", TextUnitKind.Dialogue, null, "原文\\p", null, TranslationStatus.Untranslated),
        ]);

        var qa = ProjectDashboard.Read(projeto).Qa;

        var report = VNForge.Core.Qa.QaEngine.Default.Run(VNForge.Core.Qa.QaContext.FromProject(projeto));
        Assert.True(report.UntranslatedCount > 0);
        Assert.Equal(1, qa!.Errors);
        Assert.True(qa.Warnings >= 1);
        Assert.Equal(report.OtherIssues.Count, qa.Total);
        Assert.True(qa.Total < report.Issues.Count);
    }

    [Fact]
    public void Projeto_sem_textos_nao_tem_problemas_de_conferencia()
    {
        using var temp = new TempFolder();

        Assert.Equal(new QaTotals(0, 0), ProjectDashboard.Read(NovoProjeto(temp.Path)).Qa);
    }

    [Fact]
    public void Glossario_invalido_deixa_a_conferencia_indisponivel_sem_derrubar_o_painel()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);
        File.WriteAllText(projeto.GlossaryPath, "isto nao e json");

        var painel = ProjectDashboard.Read(projeto);

        Assert.Null(painel.Qa);
        Assert.Equal(6, painel.Status.Total.Total);
    }

    [Fact]
    public void Sem_build_o_estado_e_None_e_com_texto_diferente_do_build_e_Stale()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);
        Assert.Equal(BuildStatus.None, ProjectDashboard.Read(projeto).Build);

        File.WriteAllText(
            Path.Combine(projeto.CacheDirectory, "build-state.json"),
            "{\"version\":1,\"scripts\":{},\"archives\":[],\"warnings\":[],\"textFingerprint\":\"antigo\"}");

        Assert.Equal(BuildStatus.Stale, ProjectDashboard.Read(projeto).Build);
    }

    [Fact]
    public void Sem_backup_nao_ha_data_de_backup()
    {
        using var temp = new TempFolder();

        Assert.Null(ProjectDashboard.Read(ProjetoComTextos(temp.Path)).LastBackupAt);
    }

    [Fact]
    public void Nomes_de_script_e_de_personagem_vem_dos_arquivos_do_projeto()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);
        projeto.RenameScript("yst00001", "Prólogo");
        projeto.SaveCharacters([new VNForge.Core.Glossary.CharacterEntry(" 美咲 ", "Misaki")]);

        var painel = ProjectDashboard.Read(projeto);

        Assert.Equal("Prólogo", painel.ScriptNames.DisplayName("yst00001"));
        Assert.Null(painel.ScriptNames.DisplayName("yst00002"));
        Assert.Equal("Misaki", painel.CharacterNames["美咲"]);
    }

    [Fact]
    public void Nomes_ilegiveis_nao_derrubam_o_painel()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoComTextos(temp.Path);
        File.WriteAllText(projeto.ScriptNamesPath, "isto nao e json");
        File.WriteAllText(projeto.CharactersPath, "isto nao e json");

        var painel = ProjectDashboard.Read(projeto);

        Assert.Empty(painel.ScriptNames.Entries);
        Assert.Empty(painel.CharacterNames);
        Assert.Equal(6, painel.Status.Total.Total);
    }
}
