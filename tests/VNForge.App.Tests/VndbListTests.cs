using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A lista do VNDB no app (#550): o token no cofre, a sincronização nos dois sentidos com o catálogo falso e o cartão da gaveta.</summary>
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

        public Setup(IEnumerable<LibraryGame> games, IEnumerable<VndbListEntry> remote, bool withToken = true)
        {
            Config = folder.Path;
            File = LibrarySamples.Save(Config, new GameLibrary(games));
            Fake = new FakeVndbCatalog(list: remote, time: new FixedClock(Later));
            Store = new MemoryVndbTokenStore();
            if (withToken)
            {
                Store.Save(FakeVndbCatalog.ValidToken);
            }

            Service = new VndbListService(Store, Fake, File, new FixedClock(Later), _ => null);
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

    [Fact]
    public void Cofre_que_nao_abre_vira_aviso_e_nao_derruba_a_aba()
    {
        using var folder = new TempFolder();
        var service = new VndbListService(new BrokenVault(), new FakeVndbCatalog(), new LibraryFile(folder.Path), new FixedClock(Later), _ => null);

        var connection = new VndbConnectionViewModel(service, new FixedClock(Later));

        Assert.False(connection.HasToken);
        Assert.Contains("Grave de novo", connection.TokenSourceText);
    }

    private sealed class BrokenVault : IVndbTokenStore
    {
        public string? Load() => throw new VndbException("Não foi possível abrir o token guardado. Grave de novo com 'vnf vndb token set'.");

        public void Save(string value) => throw new InvalidOperationException();

        public bool Delete() => false;
    }

    [Fact]
    public async Task Sincronizar_traz_o_que_mudou_no_VNDB_e_envia_o_que_mudou_aqui()
    {
        using var setup = new Setup([Game("g1", "v4"), Game("g2", "v5")], [Entry("v4", T0, 80, "Capítulo 4 melhora muito.", LibraryListLabels.Playing)]);
        setup.Service.Edit("g2", new VndbListUpdate { LabelsSet = [LibraryListLabels.Finished], Vote = 90 });

        var plan = await setup.Service.PlanAsync(FakeVndbCatalog.ValidToken, LibrarySyncDirection.Both, ct: Ct);
        var report = await setup.Service.ApplyAsync(plan, FakeVndbCatalog.ValidToken, Ct);

        Assert.Null(report.AbortReason);
        var pulled = setup.Local("g1");
        Assert.Equal(80, pulled.Vote);
        Assert.Equal("Capítulo 4 melhora muito.", pulled.Notes);
        Assert.True(pulled.HasLabel(LibraryListLabels.Playing));
        var pushed = await setup.Remote("v5");
        Assert.Equal(90, pushed.Vote);
        Assert.Contains(pushed.Labels, l => l.Id == LibraryListLabels.Finished);
        Assert.False(setup.File.Load().Find("g2").CurrentList!.Pending);
    }

    [Fact]
    public async Task Marcar_como_terminado_grava_aqui_e_envia_ao_VNDB_quando_ha_token()
    {
        using var setup = new Setup([Game("g1", "v4")], [Entry("v4", T0, null, null, LibraryListLabels.Playing)]);
        var plan = await setup.Service.PlanAsync(FakeVndbCatalog.ValidToken, LibrarySyncDirection.Both, ct: Ct);
        await setup.Service.ApplyAsync(plan, FakeVndbCatalog.ValidToken, Ct);
        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Playing));

        var note = await setup.Service.MarkFinishedAsync("g1", Ct);

        Assert.Null(note);
        var local = setup.Local("g1");
        Assert.True(local.HasLabel(LibraryListLabels.Finished));
        Assert.False(local.HasLabel(LibraryListLabels.Playing));
        Assert.Equal(new DateOnly(2026, 11, 6), local.Finished);
        var remote = await setup.Remote("v4");
        Assert.Contains(remote.Labels, l => l.Id == LibraryListLabels.Finished);
        Assert.DoesNotContain(remote.Labels, l => l.Id == LibraryListLabels.Playing);
        Assert.Equal(new DateOnly(2026, 11, 6), remote.Finished);
    }

    [Fact]
    public async Task Marcar_como_terminado_sem_token_fica_so_aqui()
    {
        using var setup = new Setup([Game("g1", "v4")], [], withToken: false);

        var note = await setup.Service.MarkFinishedAsync("g1", Ct);

        Assert.Contains("só aqui", note);
        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Finished));
        Assert.DoesNotContain(setup.Fake.Calls, c => c.StartsWith(nameof(IVndbUserList.UpdateEntryAsync), StringComparison.Ordinal));
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
        Assert.True(row.RemoteWins);
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

    [Fact]
    public async Task O_cartao_da_gaveta_mostra_a_lista_e_trocar_um_rotulo_grava_na_biblioteca()
    {
        using var setup = new Setup([Game("g1", "v4")], []);
        setup.Service.Edit("g1", new VndbListUpdate { LabelsSet = [LibraryListLabels.Playing], Vote = 80, Started = new DateOnly(2026, 10, 3), Notes = "Voltar na rota da Aoi." });
        var library = new LibraryViewModel(setup.File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Later)) { ListService = setup.Service };
        await library.LoadAsync();
        library.Select(library.Cards.Single());

        var card = library.Drawer!.ListCard!;
        Assert.Equal("8 de 10", card.VoteText);
        Assert.Equal("03/10/2026", card.StartedText);
        Assert.Equal("ainda não terminou", card.FinishedText);
        Assert.Equal("Mudança só aqui", card.SyncText);
        Assert.Equal(["Jogando"], card.Labels.Where(l => l.IsOn).Select(l => l.Text));

        await card.ToggleLabelCommand.ExecuteAsync(card.Labels.Single(l => l.Text == "Terminado"));

        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Finished));
        Assert.Equal(["Jogando", "Terminado"], library.Drawer!.ListCard!.Labels.Where(l => l.IsOn).Select(l => l.Text));
    }

    [Fact]
    public async Task Jogo_sem_confirmacao_no_VNDB_nao_tem_cartao_da_lista()
    {
        using var setup = new Setup([Game("g1", "v4") with { VndbId = null, Confirmation = LibraryConfirmation.None }], []);
        var library = new LibraryViewModel(setup.File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Later)) { ListService = setup.Service };
        await library.LoadAsync();
        library.Select(library.Cards.Single());

        Assert.Null(library.Drawer!.ListCard);
    }
}
