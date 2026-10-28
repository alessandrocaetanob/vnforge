using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

/// <summary>Projeto extraído antes de o título entrar na extração lê o título do <c>.cache/</c>, sem extrair de novo e sem tocar no texto (#433).</summary>
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
    public void Le_o_titulo_do_cache_e_grava_no_projeto()
    {
        var project = NewProject();
        WriteCachedTitle(project, "原題");

        var result = ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Equal("原題", result.Original);
        Assert.Null(result.Warning);
        Assert.Equal(new GameTitleConfig("原題", null), project.LoadConfig().Game.Title);
    }

    [Fact]
    public void Nao_toca_no_texto_nem_extrai_de_novo()
    {
        var project = NewProject();
        WriteCachedTitle(project, "原題");
        var unit = new VNForge.Core.Text.TextUnit("s:000000", VNForge.Core.Text.TextUnitKind.Dialogue, null, "原文", "tradução", VNForge.Core.Text.TranslationStatus.Approved);
        project.SaveText("s", [unit]);
        var before = File.ReadAllBytes(project.TextPath("s"));

        ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Equal(before, File.ReadAllBytes(project.TextPath("s")));
        Assert.False(File.Exists(project.StatePath));
    }

    [Fact]
    public void Mantem_a_traducao_ja_digitada()
    {
        var project = NewProject(new GameTitleConfig("題", "Meu titulo"));
        WriteCachedTitle(project, "原題");

        ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Equal(new GameTitleConfig("原題", "Meu titulo"), project.LoadConfig().Game.Title);
    }

    [Fact]
    public void Cache_sem_o_arquivo_do_titulo_avisa_e_nao_cria_o_campo()
    {
        var project = NewProject();
        Directory.CreateDirectory(Path.Combine(project.GameCacheDirectory, FakeEngine.PackageName));

        var result = ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Null(result.Original);
        Assert.NotNull(result.Warning);
        Assert.Null(project.LoadConfig().Game.Title);
    }

    [Fact]
    public void Projeto_sem_cache_manda_extrair_de_novo()
    {
        var project = NewProject();

        var result = ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Null(result.Original);
        Assert.Contains("extra", result.Warning, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Engine_desconhecida_vira_aviso_em_vez_de_excecao()
    {
        var project = NewProject(engine: "outra");

        var result = ProjectExtractor.RefreshGameTitle(project, [new FakeEngine()]);

        Assert.Null(result.Original);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void CanReadGameTitle_so_vale_para_engine_que_oferece_o_titulo()
    {
        var withTitle = NewProject().LoadConfig();

        Assert.True(ProjectExtractor.CanReadGameTitle(withTitle, [new FakeEngine()]));
        Assert.False(ProjectExtractor.CanReadGameTitle(withTitle with { Engine = new EngineConfig("outra") }, [new FakeEngine()]));
        Assert.False(ProjectExtractor.CanReadGameTitle(withTitle, [new NoTitleEngine("fake")]));
    }

    private sealed class NoTitleEngine(string id) : IEngine
    {
        public string Id => id;

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }
}
