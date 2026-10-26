using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectTextLockTests
{
    private const string Key = "yst00001";

    private static ProjectFolder NewProject(string root) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    [Fact]
    public void Duas_instancias_da_mesma_pasta_dividem_a_trava()
    {
        using var temp = new TempFolder();
        var first = NewProject(temp.Path);
        var second = ProjectFolder.Open(temp.Path);

        Assert.Same(ProjectTextLock.For(first), ProjectTextLock.For(second));
    }

    [Fact]
    public void Pastas_diferentes_tem_travas_diferentes()
    {
        using var one = new TempFolder();
        using var two = new TempFolder();

        Assert.NotSame(ProjectTextLock.For(NewProject(one.Path)), ProjectTextLock.For(NewProject(two.Path)));
    }

    [Fact]
    public async Task Edicoes_simultaneas_de_linhas_diferentes_nao_se_perdem()
    {
        using var temp = new TempFolder();
        var project = NewProject(temp.Path);
        project.SaveText(Key, [.. Enumerable.Range(0, 40).Select(i => new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", null, TranslationStatus.Untranslated))]);

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            new LineEditor(ProjectFolder.Open(temp.Path), TimeProvider.System).SetTranslation(Key, $"{Key}:{i:D6}", $"tradução {i}"))));

        var saved = project.LoadText(Key);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal($"tradução {i}", saved[i].Translation);
        }
    }
}
