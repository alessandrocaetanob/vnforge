using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A lista do VNDB no app (#550): o token no cofre, a sincronização nos dois sentidos com o catálogo falso e o cartão da gaveta.</summary>
[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
[Trait("Risco", "R7")]
public sealed class VndbListTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = T0.AddDays(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LibraryGame Game(string id, string vndbId) =>
        new(id, "D:\\Jogos\\" + id, null, LibraryEngine.Unknown, "Jogo " + id, null, T0) { VndbId = vndbId, Confirmation = LibraryConfirmation.Manual };

    private static VndbListEntry Entry(string id, DateTimeOffset modified, int? vote = null, string? notes = null, params int[] labels) =>
        new(id, "Jogo", T0, modified, null, vote, null, null, notes, [.. labels.Select(LibraryListLabels.Named)]);

    private sealed class Setup : IDisposable
    {
        private readonly TempFolder folder = new();

        public Setup(IEnumerable<LibraryGame> games, IEnumerable<VndbListEntry> remote, bool withToken = true, bool persistChoices = false, Func<FakeVndbCatalog, IVndbUserList>? wrap = null)
        {
            Config = folder.Path;
            File = LibrarySamples.Save(Config, new GameLibrary(games));
            Fake = new FakeVndbCatalog(list: remote, time: new FixedClock(Later));
            Store = new MemoryVndbTokenStore();
            if (withToken)
            {
                Store.Save(FakeVndbCatalog.ValidToken);
            }

            Service = new VndbListService(Store, wrap?.Invoke(Fake) ?? Fake, File, new FixedClock(Later), _ => null, persistChoices ? new VndbSyncSettingsFile(Config) : null);
        }

        public string Config { get; }

        public LibraryFile File { get; }

        public FakeVndbCatalog Fake { get; }

        public MemoryVndbTokenStore Store { get; }

        public VndbListService Service { get; }

        public LibraryListFields Local(string id) => File.Load().Find(id).CurrentList!.Fields;

        public async Task<VndbListEntry> Remote(string vndbId) =>
            (await Fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items.Single(e => e.VisualNovelId == vndbId);

        public void Dispose() => folder.Dispose();
    }

    private sealed class MemoryVndbTokenStore : IVndbTokenStore
    {
        private string? token;

        public string? Load() => token;

        public void Save(string value) => token = value;

        public bool Delete()
        {
            var had = token is not null;
            token = null;
            return had;
        }
    }

    private sealed class BrokenVault : IVndbTokenStore
    {
        public string? Load() => throw new VndbException("Não foi possível abrir o token guardado. Grave de novo com 'vnf vndb token set'.");

        public void Save(string value) => throw new InvalidOperationException();

        public bool Delete() => false;
    }

    [Fact]
    public async Task Sem_rede_terminar_aqui_fica_so_aqui_e_nao_derruba()
    {
        using var setup = new Setup([Game("g1", "v4")], [], wrap: fake => new OfflineList(fake));
        setup.Service.Choices = new VndbSyncChoices { MarkFinishedOnVndb = true };

        var note = await setup.Service.MarkFinishedAsync("g1", Ct);

        Assert.StartsWith("Gravado só aqui", note);
        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Finished));
    }

    private sealed class OfflineList(FakeVndbCatalog inner) : IVndbUserList
    {
        public Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default) => inner.GetAuthInfoAsync(token, ct);

        public Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default) => inner.GetListAsync(userId, token, ct);

        public Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default) =>
            throw new HttpRequestException("Sem conexão.");
    }

    [Fact]
    public async Task O_token_vai_so_para_o_cofre_e_um_token_recusado_nao_e_guardado()
    {
        using var setup = new Setup([Game("g1", "v4")], [Entry("v4", T0, 80, null, LibraryListLabels.Playing)], withToken: false);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));

        connection.NewToken = "token-errado";
        await connection.SaveTokenCommand.ExecuteAsync(null);
        Assert.False(connection.HasToken);
        Assert.Null(setup.Store.Load());
        Assert.Contains("não aceitou", connection.Message);
        Assert.DoesNotContain("token-errado", connection.Message);

        connection.NewToken = FakeVndbCatalog.ValidToken;
        await connection.SaveTokenCommand.ExecuteAsync(null);
        Assert.True(connection.HasToken);
        Assert.True(connection.CanWrite);
        Assert.Equal("Conectado como fake", connection.AccountText);
        Assert.Equal(string.Empty, connection.NewToken);
        Assert.DoesNotContain(FakeVndbCatalog.ValidToken, connection.TokenMask);

        await connection.SyncNowCommand.ExecuteAsync(null);
        Assert.Contains("1 jogo na lista", connection.LastSyncText);

        foreach (var path in Directory.EnumerateFiles(setup.Config, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(FakeVndbCatalog.ValidToken, await File.ReadAllTextAsync(path, Ct));
        }

        connection.RemoveTokenCommand.Execute(null);
        Assert.False(connection.HasToken);
        Assert.Null(setup.Store.Load());
    }

    [Fact]
    public async Task Campo_mudado_dos_dois_lados_abre_o_dialogo_e_so_grava_depois_de_confirmar()
    {
        var local = new LibraryListState("v4", new LibraryListFields { Vote = 90 }, T0.AddHours(1))
        {
            Base = new LibraryListFields { Vote = 80 },
            SyncedAt = T0,
            OnVndb = true,
        };
        using var setup = new Setup([Game("g1", "v4") with { List = local }], [Entry("v4", T0.AddHours(2), 70, null)]);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));
        var changed = 0;
        connection.ListChanged += (_, _) => changed++;

        await connection.SyncNowCommand.ExecuteAsync(null);

        Assert.True(connection.IsConflictsOpen);
        var row = Assert.Single(connection.Conflicts);
        Assert.Equal("voto", row.Field);
        Assert.True(row.UseRemote);
        Assert.Equal(90, setup.Local("g1").Vote);

        connection.DecideLaterCommand.Execute(null);
        Assert.False(connection.IsConflictsOpen);
        Assert.Equal(90, setup.Local("g1").Vote);
        Assert.Equal(0, changed);

        await connection.SyncNowCommand.ExecuteAsync(null);
        await connection.ApplyConflictsCommand.ExecuteAsync(null);

        Assert.Equal(70, setup.Local("g1").Vote);
        Assert.Equal(1, changed);
        Assert.False(connection.IsConflictsOpen);
    }

}
