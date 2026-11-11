using VNForge.Core.Factsheet;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Vndb;
using VNForge.Vndb;
using static VNForge.Core.Tests.Library.LibraryCharactersSamples;

namespace VNForge.Core.Tests.Library;

/// <summary>#558: a sincronização enche o registro com o catálogo falso e não volta ao VNDB pelo que já tem.</summary>
public sealed class LibraryCharacterSyncTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SheetStore Store => new(_dir.Path);

    private GameEditionsFile EditionsFile => new(_dir.Path);

    private Task<LibraryCharacterSyncResult> Sync(IVndbCatalog catalog, GameLibrary? library = null, bool again = false, IProgress<LibraryCharacterSyncProgress>? progress = null) =>
        LibraryCharacterSync.SyncAsync(catalog, library ?? OwnedLibrary(), Store, EditionsFile, again, new FixedClock(Now), progress, Ct);

    [Fact]
    public async Task Jogo_que_o_VNDB_nao_tem_vira_falha_e_os_outros_seguem()
    {
        var library = new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g8", "Ghost", "v1", "Ghost")]);
        var reports = new List<LibraryCharacterSyncProgress>();

        var result = await Sync(Catalog(), library, progress: new Collect(reports));

        Assert.Equal(1, result.Fetched);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(("v1", "O VNDB não tem o jogo v1."), (failure.VisualNovelId, failure.Message));
        Assert.Equal([1, 2], reports.Select(r => r.Done));
        Assert.All(reports, r => Assert.Equal(2, r.Total));
        Assert.NotNull(Store.LoadCharacter("c101"));
    }

    [Fact]
    public async Task Falha_do_VNDB_num_jogo_nao_para_os_outros()
    {
        var catalog = new FlakyCatalog(Catalog(), Star);

        var result = await Sync(catalog);

        Assert.Equal(1, result.Fetched);
        Assert.Equal([Star], result.Failures.Select(f => f.VisualNovelId));
        Assert.Contains("fora do ar", result.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelar_interrompe_antes_de_perguntar()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => LibraryCharacterSync.SyncAsync(Catalog(), OwnedLibrary(), Store, EditionsFile, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task A_ida_e_volta_completa_com_o_registro_em_disco_mostra_os_dois_jogos_e_esconde_o_de_fora()
    {
        await Sync(Catalog());

        var list = LibraryCharacters.List(OwnedLibrary(), Store.LoadAllCharacters(), Store.LoadTerms(), EditionsFile.Load(), new LibraryPrivacy(ShowSpoilers: true));

        var hana = Assert.Single(list.Characters, c => c.Id == "c101");
        Assert.Equal([("Moon Flower", "protagonista"), ("Star Garden", "secundário")], hana.AppearsIn.Select(a => (a.GameTitle, a.Role)));
        var mika = Assert.Single(list.Characters, c => c.Id == "c102");
        Assert.Equal(("main", 2), (mika.AppearsIn[0].RoleCode, mika.AppearsIn[0].Spoiler));
    }

    [Fact]
    public async Task Ficha_guardada_sem_as_edicoes_nao_conta_como_guardada_e_e_buscada_de_novo()
    {
        await Sync(Catalog());
        Store.Update(s =>
        {
            var hana = s.LoadCharacter("c101")!;
            s.SaveCharacter(hana with { Appearances = [.. hana.Appearances.Select(a => a with { Spoiler = 0, Editions = null })] });
        });
        var catalog = Catalog();

        var result = await Sync(catalog);

        Assert.Equal((2, 1, 1), (result.Games, result.Fetched, result.Cached));
        Assert.NotNull(Store.LoadCharacter("c101")!.In(Star)!.Editions);
        Assert.Equal(1, Store.LoadCharacter("c101")!.In(Star)!.WorstSpoiler);
    }

    private sealed class Collect(List<LibraryCharacterSyncProgress> reports) : IProgress<LibraryCharacterSyncProgress>
    {
        public void Report(LibraryCharacterSyncProgress value) => reports.Add(value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FlakyCatalog(IVndbCatalog inner, string failing) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) => inner.SearchVisualNovelsAsync(title, limit, ct);

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) =>
            id == failing ? throw new VndbException("O VNDB está fora do ar.") : inner.GetVisualNovelAsync(id, ct);

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => inner.SearchCharactersByTitleAsync(title, ct);

        public Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) => inner.GetReleasesAsync(visualNovelId, ct);
    }
}
