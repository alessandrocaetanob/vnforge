using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

/// <summary>A extração lê o título original do jogo e o guarda no <c>vnforge.json</c> sem perder a tradução (#427).</summary>
public sealed class GameTitleExtractionTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;

    public GameTitleExtractionTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "pacote.fake"), "dialogue|Oi");
    }

    public void Dispose() => _temp.Dispose();

    private ProjectFolder NewProject(GameTitleConfig? title = null) =>
        ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe") { Title = title },
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));

    private void WriteTitle(string title) => File.WriteAllText(Path.Combine(_game, FakeEngine.TitleFileName), title);

    [Fact]
    public void Extract_de_novo_mantem_a_traducao_do_titulo()
    {
        WriteTitle("原題");
        var project = NewProject(new GameTitleConfig("原題", "Meu titulo"));

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new GameTitleConfig("原題", "Meu titulo"), project.LoadConfig().Game.Title);
    }
}
