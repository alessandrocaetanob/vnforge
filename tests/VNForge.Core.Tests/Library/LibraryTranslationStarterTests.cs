using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R6")]
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
    public async Task Engine_que_o_vnforge_ainda_nao_traduz_e_recusada_antes_de_criar_qualquer_coisa()
    {
        var game = NewGame(engine: new LibraryEngine("kirikiri", "KiriKiri", LibraryEngineSource.Signature));
        var library = new GameLibrary([game]);

        var error = await Assert.ThrowsAsync<LibraryException>(() => LibraryTranslationStarter.StartAsync(library, game, ProjectFolderPath, [_engine], Catalog(), cancellationToken: Ct));

        Assert.Contains("KiriKiri", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(ProjectFolderPath));
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
}
