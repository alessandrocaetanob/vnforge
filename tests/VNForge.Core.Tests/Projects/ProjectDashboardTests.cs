using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R6")]
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
}
