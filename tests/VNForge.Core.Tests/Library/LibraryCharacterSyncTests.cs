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
    public async Task Traz_as_fichas_dos_jogos_da_biblioteca_e_casa_a_edicao_de_cada_pasta()
    {
        var catalog = Catalog();

        var result = await Sync(catalog);

        Assert.Equal((2, 2, 0, 4, 2), (result.Games, result.Fetched, result.Cached, result.Characters, result.EditionsMatched));
        Assert.Empty(result.Failures);
        Assert.Equal(["c101", "c102", "c104"], Store.LoadAllCharacters().Select(c => c.Id));
        Assert.DoesNotContain(catalog.Calls, c => c.Contains(Outside, StringComparison.Ordinal));
        var editions = EditionsFile.Load();
        Assert.Equal(["r1"], editions.Of("g1")!.ReleaseIds);
        Assert.Equal(["r3"], editions.Of("g2")!.ReleaseIds);
        Assert.Equal(Now, editions.Of("g1")!.MatchedAt);
    }

    [Fact]
    public async Task Segunda_rodada_usa_o_que_esta_guardado_e_nao_pergunta_ao_VNDB_de_novo()
    {
        await Sync(Catalog());
        var second = Catalog();

        var result = await Sync(second);

        Assert.Equal((2, 0, 2, 0, 0), (result.Games, result.Fetched, result.Cached, result.Characters, result.EditionsMatched));
        Assert.Empty(second.Calls);
    }

    [Fact]
    public async Task Again_busca_tudo_de_novo_e_mantem_as_traducoes()
    {
        await Sync(Catalog());
        Store.Update(s =>
        {
            var hana = s.LoadCharacter("c101")!;
            s.SaveCharacter(hana with { Description = hana.Description!.TranslatedByAi("Uma florista. [spoiler]Ela é um fantasma.[/spoiler]") });
        });
        var catalog = Catalog();

        var result = await Sync(catalog, again: true);

        Assert.Equal((2, 0), (result.Fetched, result.Cached));
        Assert.Contains("GetCharactersAsync(" + Moon + ")", catalog.Calls);
        Assert.Contains("GetReleasesAsync(" + Moon + ")", catalog.Calls);
        Assert.Equal("Uma florista. [spoiler]Ela é um fantasma.[/spoiler]", Store.LoadCharacter("c101")!.Description!.Target);
    }

    [Fact]
    public async Task Mudar_o_jogo_confirmado_refaz_o_casamento_da_edicao()
    {
        await Sync(Catalog());
        var changed = new GameLibrary([Owned("g1", "Star Garden", Star, "Star Garden")]);
        var catalog = Catalog();

        var result = await Sync(catalog, changed);

        Assert.Equal(1, result.EditionsMatched);
        Assert.Equal(Star, EditionsFile.Load().Of("g1")!.VisualNovelId);
        Assert.Equal(["r3"], EditionsFile.Load().Of("g1")!.ReleaseIds);
        Assert.Contains("GetReleasesAsync(" + Star + ")", catalog.Calls);
    }

    [Fact]
    public async Task Dois_itens_do_mesmo_jogo_pedem_as_edicoes_uma_vez_so()
    {
        var library = new GameLibrary([Owned("g1", "Moon Flower", Moon, "Moon Flower"), Owned("g9", "Moon Flower Deluxe Pack", Moon, "Moon Flower Deluxe Pack")]);
        var catalog = Catalog();

        var result = await Sync(catalog, library);

        Assert.Equal(1, result.Games);
        Assert.Equal(1, catalog.Calls.Count(c => c.StartsWith("GetReleasesAsync", StringComparison.Ordinal)));
        Assert.Equal(["r1"], EditionsFile.Load().Of("g1")!.ReleaseIds);
        Assert.Equal(["r2"], EditionsFile.Load().Of("g9")!.ReleaseIds);
    }

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
    public async Task Biblioteca_sem_jogo_confirmado_nao_pergunta_nada()
    {
        var catalog = Catalog();

        var result = await Sync(catalog, new GameLibrary([new LibraryGame("g5", "C:\\Jogos\\X", null, LibraryEngine.Unknown, "X", null, Now)]));

        Assert.Equal(0, result.Games);
        Assert.Empty(catalog.Calls);
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

    [Fact]
    public async Task Lista_de_edicoes_cortada_grava_o_casamento_parcial_marcado_como_incompleto_sem_falha()
    {
        var catalog = new TruncatedReleases(Catalog());

        var result = await Sync(catalog);

        Assert.Empty(result.Failures);
        Assert.Equal((2, 2), (result.EditionsMatched, result.EditionsIncomplete));
        var editions = EditionsFile.Load();
        Assert.Equal(["g1", "g2"], editions.GameIds);
        Assert.All(editions.GameIds, id => Assert.True(editions.Of(id)!.Incomplete));
        Assert.Equal(["r1"], editions.Of("g1")!.ReleaseIds);
    }

    [Fact]
    public async Task Casamento_incompleto_nao_e_refeito_a_cada_rodada_mas_o_again_o_refaz()
    {
        await Sync(new TruncatedReleases(Catalog()));
        var second = Catalog();

        var plain = await Sync(second);
        var again = await Sync(Catalog(), again: true);

        Assert.DoesNotContain(second.Calls, c => c.StartsWith("GetReleasesAsync", StringComparison.Ordinal));
        Assert.Equal(0, plain.EditionsIncomplete);
        Assert.All(EditionsFile.Load().GameIds, id => Assert.False(EditionsFile.Load().Of(id)!.Incomplete));
        Assert.Equal(0, again.EditionsIncomplete);
    }

    private sealed class Collect(List<LibraryCharacterSyncProgress> reports) : IProgress<LibraryCharacterSyncProgress>
    {
        public void Report(LibraryCharacterSyncProgress value) => reports.Add(value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TruncatedReleases(IVndbCatalog inner) : IVndbCatalog
    {
        public Task<IReadOnlyList<VndbVisualNovel>> SearchVisualNovelsAsync(string title, int limit = 10, CancellationToken ct = default) => inner.SearchVisualNovelsAsync(title, limit, ct);

        public Task<VndbVisualNovel?> GetVisualNovelAsync(string id, CancellationToken ct = default) => inner.GetVisualNovelAsync(id, ct);

        public Task<VndbResults<VndbCharacter>> GetCharactersAsync(string visualNovelId, CancellationToken ct = default) => inner.GetCharactersAsync(visualNovelId, ct);

        public Task<VndbResults<VndbCharacter>> SearchCharactersByTitleAsync(string title, CancellationToken ct = default) => inner.SearchCharactersByTitleAsync(title, ct);

        public async Task<VndbResults<VndbRelease>> GetReleasesAsync(string visualNovelId, CancellationToken ct = default) =>
            (await inner.GetReleasesAsync(visualNovelId, ct)) with { Truncated = true };
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
