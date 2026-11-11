using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectStatusReporterTests
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
        projeto.SaveText("yst00002",
        [
            Linha("yst00002", 1, "健", TranslationStatus.Reviewed),
            Linha("yst00002", 2, "美咲", TranslationStatus.Machine),
            Linha("yst00002", 3, null, TranslationStatus.Untranslated),
        ]);
        projeto.SaveText("yst00003", []);
        return projeto;
    }

    [Fact]
    public void Personagens_ordenam_por_linhas_e_sem_falante_vem_por_ultimo()
    {
        using var temp = new TempFolder();
        var status = ProjectStatusReporter.Create(ProjetoComTextos(temp.Path));

        Assert.Equal(["美咲", "健", null], status.Characters.Select(c => c.Speaker));
        Assert.Equal(new StatusCounts(1, 2, 0, 0, 0), status.Characters[0].Counts);
        Assert.Equal(2, status.Characters[2].Counts.Total);
    }

    [Fact]
    public void Projeto_sem_textos_da_tudo_zerado()
    {
        using var temp = new TempFolder();
        var status = ProjectStatusReporter.Create(NovoProjeto(temp.Path));

        Assert.Equal(0, status.Total.Total);
        Assert.Empty(status.Files);
        Assert.Empty(status.Characters);
        Assert.Equal(0, status.Total.Percent);
    }
}
