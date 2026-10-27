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
    public void Extract_guarda_o_titulo_original_no_projeto()
    {
        WriteTitle("原題");
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new GameTitleConfig("原題", null), project.LoadConfig().Game.Title);
        Assert.Equal("原題", result.GameTitle);
    }

    [Fact]
    public void Extract_de_novo_mantem_a_traducao_do_titulo()
    {
        WriteTitle("原題");
        var project = NewProject(new GameTitleConfig("原題", "Meu titulo"));

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new GameTitleConfig("原題", "Meu titulo"), project.LoadConfig().Game.Title);
    }

    [Fact]
    public void Extract_com_o_titulo_mudado_no_jogo_atualiza_o_original_e_mantem_a_traducao()
    {
        WriteTitle("新題");
        var project = NewProject(new GameTitleConfig("原題", "Meu titulo"));

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new GameTitleConfig("新題", "Meu titulo"), project.LoadConfig().Game.Title);
    }

    [Fact]
    public void Extract_de_jogo_sem_titulo_nao_cria_o_campo()
    {
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(project.LoadConfig().Game.Title);
        Assert.Null(result.GameTitle);
    }

    [Fact]
    public void Extract_de_jogo_que_perdeu_o_titulo_mantem_o_que_o_projeto_tinha()
    {
        var project = NewProject(new GameTitleConfig("原題", "Meu titulo"));

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new GameTitleConfig("原題", "Meu titulo"), project.LoadConfig().Game.Title);
    }
}
