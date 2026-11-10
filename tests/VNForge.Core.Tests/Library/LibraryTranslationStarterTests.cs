using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

public sealed class LibraryTranslationStarterTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _root = new();
    private readonly SyntheticGames.MarkerEngine _engine = new();

    public void Dispose() => _root.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string GameFolder => Path.Combine(_root.Path, "jogos", "Sakura");

    private string ProjectFolderPath => Path.Combine(_root.Path, "projetos", "sakura");

    private LibraryGame NewGame(string? vndbId = "v1", LibraryEngine? engine = null)
    {
        SyntheticGames.File(Path.Combine(GameFolder, SyntheticGames.MarkerFile));
        SyntheticGames.File(Path.Combine(GameFolder, "Sakura.exe"));
        return new LibraryGame(
            GameLibrary.IdFor(GameFolder),
            GameLibrary.NormalizeFolder(GameFolder),
            "Sakura.exe",
            engine ?? new LibraryEngine("marker", "Marcador", LibraryEngineSource.Engine),
            "Sakura no Uta",
            null,
            T0)
        {
            VndbId = vndbId,
            Confirmation = vndbId is null ? LibraryConfirmation.None : LibraryConfirmation.Manual,
        };
    }

    private static FakeVndbCatalog Catalog() =>
        new(
            [SyntheticGames.Vn("v1", "Sakura no Uta", description: "Uma história de primavera.")],
            [new VndbCharacter("c1", "Yuu", "優", [], "Protagonista.", null, new VndbGender("m", "m"), [new VndbCharacterAppearance("v1", "main", 0, null)], [])]);

    [Fact]
    public async Task Cria_o_projeto_ja_ligado_ao_vndb_e_liga_o_jogo_da_biblioteca_a_ele()
    {
        var game = NewGame();
        var library = new GameLibrary([game]);

        var result = await LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct);

        var project = ProjectFolder.Open(ProjectFolderPath);
        var config = project.LoadConfig();
        Assert.Equal("Sakura no Uta", config.Name);
        Assert.Equal("Sakura.exe", config.Game.Exe);
        Assert.Equal("marker", config.Engine.Id);
        var link = project.LoadVndbLink();
        Assert.Equal("v1", link?.VisualNovelId);
        Assert.Equal("Uma história de primavera.", link?.Synopsis);
        Assert.Equal("c1", Assert.Single(link!.Characters).Id);
        Assert.Equal(link.VisualNovelId, result.Vndb?.VisualNovelId);
        Assert.Equal(GameLibrary.NormalizeFolder(ProjectFolderPath), library.Find(game.Id).ProjectPath);
        Assert.Equal(library.Find(game.Id), result.Game);
        Assert.Equal(TranslationQueueState.InProgress, TranslationQueue.Describe(result.Game).State);
    }

    [Fact]
    public async Task Jogo_sem_casamento_confirmado_cria_o_projeto_sem_ficha_e_nao_consulta_o_vndb()
    {
        var game = NewGame(vndbId: null);
        var library = new GameLibrary([game]);
        var fake = Catalog();

        var result = await LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], fake, cancellationToken: Ct);

        Assert.Null(result.Vndb);
        Assert.Null(ProjectFolder.Open(ProjectFolderPath).LoadVndbLink());
        Assert.Empty(fake.Calls);
        Assert.NotNull(library.Find(game.Id).ProjectPath);
    }

    [Fact]
    public async Task O_vndb_sem_o_jogo_nao_deixa_projeto_pela_metade()
    {
        var game = NewGame("v99");
        var library = new GameLibrary([game]);

        await Assert.ThrowsAsync<VndbLinkException>(() => LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct));

        Assert.False(Directory.Exists(ProjectFolderPath));
        Assert.Null(library.Find(game.Id).ProjectPath);
    }

    [Fact]
    public async Task Engine_que_o_vnforge_ainda_nao_traduz_e_recusada_antes_de_criar_qualquer_coisa()
    {
        var game = NewGame(engine: new LibraryEngine("kirikiri", "KiriKiri", LibraryEngineSource.Signature));
        var library = new GameLibrary([game]);

        var error = await Assert.ThrowsAsync<LibraryException>(() => LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct));

        Assert.Contains("KiriKiri", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(ProjectFolderPath));
    }

    [Fact]
    public async Task Jogo_que_ja_tem_projeto_nao_ganha_outro()
    {
        var game = NewGame();
        var library = new GameLibrary([game]);
        await LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct);

        var error = await Assert.ThrowsAsync<LibraryException>(
            () => LibraryTranslationStarter.StartAsync(library, library.Find(game.Id), Path.Combine(_root.Path, "projetos", "outro"), [_engine], Catalog(), cancellationToken: Ct));

        Assert.Contains("já tem o projeto", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root.Path, "projetos", "outro")));
    }

    [Fact]
    public async Task Projeto_ligado_que_sumiu_do_disco_tambem_recusa_e_diz_como_desligar()
    {
        var game = NewGame() with { ProjectPath = GameLibrary.NormalizeFolder(Path.Combine(_root.Path, "projetos", "apagado")) };
        var library = new GameLibrary([game]);

        var error = await Assert.ThrowsAsync<LibraryException>(() => LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct));

        Assert.Contains("vnf library link", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(ProjectFolderPath));
    }

    [Fact]
    public async Task As_escolhas_da_pessoa_valem_mais_que_as_do_jogo()
    {
        var game = NewGame();
        var library = new GameLibrary([game]);

        await LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), new ProjectInitOptions(Name: "Minha tradução", TargetLanguage: "pt-PT"), cancellationToken: Ct);

        var config = ProjectFolder.Open(ProjectFolderPath).LoadConfig();
        Assert.Equal("Minha tradução", config.Name);
        Assert.Equal("pt-PT", config.Languages.Target);
    }

    [Theory]
    [InlineData("Sakura no Uta", "Sakura no Uta")]
    [InlineData("Fate/stay night", "Fate stay night")]
    [InlineData("  Alive...  ", "Alive")]
    [InlineData("///", "g1")]
    public void O_nome_da_pasta_sugerida_sai_do_titulo_sem_o_que_o_sistema_de_arquivos_recusa(string title, string expected)
    {
        var game = NewGame() with { Id = "g1", Title = title };

        Assert.Equal(expected, LibraryTranslationStarter.SuggestedFolderName(game));
    }
}
