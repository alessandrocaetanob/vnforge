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
    public void Pasta_de_backup_sem_manifesto_nao_completa_a_etapa_Injetar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.CreateDirectory(Path.Combine(projeto.BackupDirectory, "2026-10-24_1200"));

        Assert.False(ProjectDashboard.Read(projeto).Stages.Single(s => s.Key == PipelineStageKey.Inject).IsComplete);
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
    public void So_avisos_de_estilo_nao_pedem_revisao_no_painel_mas_ficam_contados()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveConfig(projeto.LoadConfig() with { Style = StyleConfig.Default with { Honorifics = HonorificsPolicy.Remove } });
        projeto.SaveCharacters([new VNForge.Core.Glossary.CharacterEntry("美咲", "Misaki")]);
        projeto.SaveText("yst00001",
        [
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, "美咲", "原文", "Misaki-chan, espera!", TranslationStatus.Translated),
            new TextUnit("yst00001:000002", TextUnitKind.Dialogue, "美咲", "原文二", "Vamos.", TranslationStatus.Translated),
        ]);

        var qa = ProjectDashboard.Read(projeto).Qa;

        Assert.Equal(new QaTotals(0, 0) { StyleAdvice = 2 }, qa);
        Assert.Equal(0, qa!.Total);
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
