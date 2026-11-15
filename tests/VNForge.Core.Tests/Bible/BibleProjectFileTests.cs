using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Bible;

[Trait("Risco", "R2")]
public sealed class BibleProjectFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public BibleProjectFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Minha VN",
                new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
                new EngineConfig("yuris", 500, "0xD36FAC96"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static GameBible Bible(string name = "Atraso no portão") => GameBible.From([new BibleScene("yst1") { Name = name, Summary = "Resumo." }]);

    [Fact]
    public void Biblia_ilegivel_nao_derruba_quem_so_consulta()
    {
        File.WriteAllText(_project.BiblePath, "{ quebrado");

        var bible = _project.LoadBibleOrEmpty(out var problem);

        Assert.True(bible.IsEmpty);
        Assert.Contains("bible.json", problem, StringComparison.Ordinal);
        Assert.Throws<GlossaryFileException>(_project.LoadBible);
        Assert.NotNull(_project.LoadScriptNames());
    }
}
