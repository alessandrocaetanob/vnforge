using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R7")]
public sealed class LibraryListSynchronizerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = T0.AddDays(5);
    private static readonly DateTimeOffset Latest = T0.AddDays(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LibraryGame Game(string id = "g1", string? vndbId = "v4") =>
        new(id, "C:\\Jogos\\" + id, null, LibraryEngine.Unknown, "Jogo " + id, null, T0)
        {
            VndbId = vndbId,
            Confirmation = vndbId is null ? LibraryConfirmation.None : LibraryConfirmation.Manual,
        };

    private static VndbListEntry Entry(
        string id = "v4",
        DateTimeOffset? lastModified = null,
        int? vote = null,
        string? notes = null,
        DateOnly? started = null,
        DateOnly? finished = null,
        params int[] labels) =>
        new(
            id,
            "Jogo",
            T0,
            lastModified ?? T0,
            vote is null ? null : T0,
            vote,
            started,
            finished,
            notes,
            [.. labels.Select(l => new VndbListLabel(l, l == 7 ? "Voted" : LibraryListLabels.Named(l).Label))]);

    private static (GameLibrary Library, FakeVndbCatalog Fake) Setup(VndbListEntry? entry) =>
        (new GameLibrary([Game()]), new FakeVndbCatalog(list: entry is null ? [] : [entry]));

    private static async Task<LibrarySyncReport> Sync(GameLibrary library, IVndbUserList vndb, LibrarySyncDirection direction = LibrarySyncDirection.Both, string? gameId = null, bool recreate = false)
    {
        var synchronizer = new LibraryListSynchronizer(vndb, new FakeTimeProvider(Latest));
        var plan = await synchronizer.PlanAsync(library, FakeVndbCatalog.ValidToken, new LibrarySyncOptions { Direction = direction, GameId = gameId, RecreateRemoved = recreate }, Ct);
        return await synchronizer.ApplyAsync(plan, FakeVndbCatalog.ValidToken, (game, state) => LibraryListSynchronizer.Keep(library, game, state), null, Ct);
    }

    private static async Task<VndbListEntry?> Remote(FakeVndbCatalog fake, string id = "v4") =>
        (await fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items.FirstOrDefault(e => e.VisualNovelId == id);

    private static VndbListUpdate Edit(int? vote = null, int[]? set = null, int[]? unset = null, string? notes = null) =>
        new()
        {
            Vote = vote is { } v ? VndbChange<int>.Set(v) : default,
            Notes = notes is null ? default : VndbChange<string>.Set(notes),
            LabelsSet = set ?? [],
            LabelsUnset = unset ?? [],
        };

    private static GameLibrary Synced(LibraryListFields fields, DateTimeOffset modified, LibraryListFields? baseFields = null) =>
        new([Game() with { List = new LibraryListState("v4", fields, modified) { Base = baseFields ?? fields, SyncedAt = T0, OnVndb = true } }]);

    private static LibraryListFields Fields(int? vote = null, string? notes = null, params int[] labels) =>
        LibraryListFields.Normalize(new LibraryListFields { Vote = vote, Notes = notes, Labels = [.. labels.Select(LibraryListLabels.Named)] });

    [Fact]
    public async Task Filtro_por_jogo_sincroniza_so_ele_e_recusa_jogo_sem_confirmacao()
    {
        var library = new GameLibrary([Game("g1", "v4"), Game("g2", "v17"), Game("g3", null)]);
        var fake = new FakeVndbCatalog(list: [Entry("v4", vote: 80), Entry("v17", vote: 60)]);

        var report = await Sync(library, fake, gameId: "g2");

        Assert.Equal("g2", Assert.Single(report.Items).Game.Id);
        Assert.Null(library.Games[0].List);
        await Assert.ThrowsAsync<LibraryException>(() => Sync(library, fake, gameId: "g3"));
        await Assert.ThrowsAsync<LibraryException>(() => Sync(library, fake, gameId: "g9"));
    }

    [Fact]
    public async Task Token_sem_listwrite_recusa_o_plano_que_envia_mas_deixa_trazer()
    {
        var (library, fake) = Setup(Entry(vote: 80));
        var readOnly = new PermissionsOnly(fake, ["listread"]);
        library.Replace(library.Games[0] with { List = LibraryListState.Edit(null, "v4", Edit(vote: 50), Later) });

        await Assert.ThrowsAsync<LibraryException>(() => Sync(library, readOnly));
        var pull = await Sync(library, readOnly, LibrarySyncDirection.Pull);

        Assert.Single(pull.Items);
    }

    [Fact]
    public async Task Token_sem_listread_e_recusado()
    {
        var (library, fake) = Setup(Entry(vote: 80));

        var ex = await Assert.ThrowsAsync<LibraryException>(() => Sync(library, new PermissionsOnly(fake, ["listwrite"])));

        Assert.Contains("listread", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_invalido_para_a_sincronizacao_inteira_antes_de_ler()
    {
        var (library, fake) = Setup(Entry(vote: 80));
        var synchronizer = new LibraryListSynchronizer(fake);

        var ex = await Assert.ThrowsAsync<VndbException>(() => synchronizer.PlanAsync(library, "token-errado", new LibrarySyncOptions(), Ct));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(["GetAuthInfoAsync(***)"], fake.Calls);
    }

    private sealed class PermissionsOnly(FakeVndbCatalog inner, string[] permissions) : IVndbUserList
    {
        public async Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default) =>
            (await inner.GetAuthInfoAsync(token, ct).ConfigureAwait(false)) with { Permissions = permissions };

        public Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default) =>
            inner.GetListAsync(userId, token, ct);

        public Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default) =>
            inner.UpdateEntryAsync(token, visualNovelId, update, ct);
    }
}
