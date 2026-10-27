using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.Core.Tests.Projects;

/// <summary>O título da janela do jogo no build: grava a tradução com o codificador do projeto e recusa a que a estratégia não grava (#427).</summary>
public sealed class ProjectBuilderTitleTests : IDisposable
{
    private const string OriginalTitle = "原題";
    private const string Translated = "Titulo novo";
    private const string PackageFake = "pacote.fake";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly FakeEngine _engine = new();
    private ProjectFolder _project = null!;

    public ProjectBuilderTitleTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá");
    }

    public void Dispose() => _temp.Dispose();

    private ProjectFolder Open(string strategy = "cp1252", string? translation = null, bool withTitleFile = true)
    {
        if (withTitleFile)
        {
            File.WriteAllText(Path.Combine(_game, FakeEngine.TitleFileName), OriginalTitle);
        }

        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe") { Title = new GameTitleConfig(OriginalTitle, translation) },
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig(strategy),
                AiConfig.Default));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
        return _project;
    }

    private void SetTranslation(string? translation)
    {
        var config = _project.LoadConfig();
        _project.SaveConfig(config with { Game = config.Game with { Title = config.Game.Title! with { Translation = translation } } });
    }

    private BuildResult Build(bool onlyChanged = false) =>
        ProjectBuilder.Build(_project, [_engine], qa: new QaEngine([]), options: new BuildOptions(OnlyChanged: onlyChanged), cancellationToken: TestContext.Current.CancellationToken);

    private string BuiltPackage() => File.ReadAllText(Path.Combine(_project.BuildDirectory, PackageFake));

    [Fact]
    public void Sem_traducao_do_titulo_o_pacote_sai_com_o_titulo_original()
    {
        Open();

        var result = Build();

        Assert.Null(result.GameTitle);
        Assert.Empty(_engine.LastRebuilt);
        Assert.EndsWith("\u0001" + OriginalTitle, BuiltPackage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Traducao_em_branco_conta_como_sem_traducao()
    {
        Open(translation: "   ");

        Assert.Null(Build().GameTitle);
        Assert.Empty(_engine.LastRebuilt);
    }

    [Fact]
    public void Grava_o_titulo_traduzido_no_pacote()
    {
        Open(translation: Translated);

        var result = Build();

        Assert.Equal(Translated, result.GameTitle);
        var rebuilt = Assert.Single(_engine.LastRebuilt);
        Assert.EndsWith(FakeEngine.TitleFileName, rebuilt.Path, StringComparison.Ordinal);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(OriginalTitle), rebuilt.Original);
        Assert.EndsWith("\u0001" + Translated, BuiltPackage(), StringComparison.Ordinal);
        Assert.Empty(result.RebuiltScripts);
    }

    [Fact]
    public void Grava_o_titulo_sem_os_espacos_das_pontas()
    {
        Open(translation: "  " + Translated + " ");

        Assert.Equal(Translated, Build().GameTitle);
    }

    [Fact]
    public void Recusa_titulo_que_a_estrategia_nao_grava_e_nao_toca_o_build()
    {
        Open(strategy: "sjis", translation: "Título");

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains("título", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("í", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
        Assert.Equal(0, _engine.RepackCalls);
    }

    [Fact]
    public void Sem_o_arquivo_do_titulo_no_cache_manda_rodar_o_extract()
    {
        Open(translation: Translated, withTitleFile: false);

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains("vnf extract", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_incremental_regrava_o_pacote_quando_so_o_titulo_muda()
    {
        Open(translation: Translated);
        Build(onlyChanged: true);

        SetTranslation("Outro titulo");
        var result = Build(onlyChanged: true);

        Assert.Equal("Outro titulo", result.GameTitle);
        Assert.EndsWith("\u0001Outro titulo", BuiltPackage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_incremental_sem_mudanca_reaproveita_o_pacote_e_mantem_o_titulo()
    {
        Open(translation: Translated);
        Build(onlyChanged: true);
        var repacks = _engine.RepackCalls;

        var result = Build(onlyChanged: true);

        Assert.Equal(repacks, _engine.RepackCalls);
        Assert.EndsWith("\u0001" + Translated, BuiltPackage(), StringComparison.Ordinal);
        Assert.Equal(Translated, result.GameTitle);
    }

    [Fact]
    public void Build_incremental_volta_ao_original_quando_a_traducao_do_titulo_e_apagada()
    {
        Open(translation: Translated);
        Build(onlyChanged: true);

        SetTranslation(null);
        var result = Build(onlyChanged: true);

        Assert.Null(result.GameTitle);
        Assert.EndsWith("\u0001" + OriginalTitle, BuiltPackage(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_painel_ve_o_build_como_atual_e_depois_desatualizado_quando_o_titulo_muda()
    {
        Open(translation: Translated);
        Build();
        Assert.Equal(BuildStatus.Current, ProjectDashboard.Read(_project).Build);

        SetTranslation("Outro titulo");

        Assert.Equal(BuildStatus.Stale, ProjectDashboard.Read(_project).Build);
    }
}
