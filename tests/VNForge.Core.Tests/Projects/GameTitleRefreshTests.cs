using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

/// <summary>Projeto extraído antes de o título entrar na extração lê o título do <c>.cache/</c>, sem extrair de novo e sem tocar no texto (#433).</summary>
[Trait("Risco", "R4")]
public sealed class GameTitleRefreshTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public GameTitleRefreshTests()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "jogo"));
    }

    public void Dispose() => _temp.Dispose();

    private ProjectFolder NewProject(GameTitleConfig? title = null, string engine = "fake") =>
        ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("../jogo", "Jogo.exe") { Title = title },
                new EngineConfig(engine),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));

    private static void WriteCachedTitle(ProjectFolder project, string title)
    {
        var folder = Path.Combine(project.GameCacheDirectory, FakeEngine.PackageName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FakeEngine.TitleFileName), title);
    }

    [Fact]
    public void Arquivo_do_titulo_ilegivel_vira_aviso_e_o_projeto_fica_como_esta()
    {
        var project = NewProject(new GameTitleConfig("原題", "Meu titulo"));
        WriteCachedTitle(project, "原題");
        var engine = new FakeEngine { TitleFailure = new VNForgeException("o yscfg.ybn é de uma versão que não conheço.") };

        var result = ProjectExtractor.RefreshGameTitle(project, [engine]);

        Assert.Null(result.Original);
        Assert.Contains("yscfg.ybn", result.Warning, StringComparison.Ordinal);
        Assert.Equal(new GameTitleConfig("原題", "Meu titulo"), project.LoadConfig().Game.Title);
    }
}
