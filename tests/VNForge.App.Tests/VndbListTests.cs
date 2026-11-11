using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Projects;
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

    [Fact]
    public async Task Atualizar_com_token_pergunta_ao_VNDB_de_quem_e_e_sem_token_so_limpa_o_estado()
    {
        using var withToken = new Setup([Game("g1", "v4")], []);
        var connection = new VndbConnectionViewModel(withToken.Service, new FixedClock(Later));

        await connection.RefreshAsync();

        Assert.True(connection.HasToken);
        Assert.Equal("Trocar token", connection.ChangeTokenLabel);
        Assert.True(connection.CanRemoveToken);
        Assert.False(connection.HasAccountError);

        using var withoutToken = new Setup([Game("g1", "v4")], [], withToken: false);
        var empty = new VndbConnectionViewModel(withoutToken.Service, new FixedClock(Later));

        await empty.RefreshAsync();

        Assert.False(empty.HasToken);
        Assert.False(empty.HasAccountError);
    }

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

    [Fact]
    public void Cofre_que_nao_abre_vira_aviso_e_nao_derruba_a_aba()
    {
        using var folder = new TempFolder();
        var service = new VndbListService(new BrokenVault(), new FakeVndbCatalog(), new LibraryFile(folder.Path), new FixedClock(Later), _ => null);

        var connection = new VndbConnectionViewModel(service, new FixedClock(Later));

        Assert.False(connection.HasToken);
        Assert.Contains("Grave de novo", connection.TokenSourceText);
        Assert.Equal("Guardar um token", connection.ChangeTokenLabel);
        Assert.False(connection.CanRemoveToken);
    }

    private sealed class BrokenVault : IVndbTokenStore
    {
        public string? Load() => throw new VndbException("Não foi possível abrir o token guardado. Grave de novo com 'vnf vndb token set'.");

        public void Save(string value) => throw new InvalidOperationException();

        public bool Delete() => false;
    }

    [Fact]
    public async Task Marcar_como_terminado_grava_aqui_e_envia_ao_VNDB_quando_ha_token()
    {
        using var setup = new Setup([Game("g1", "v4")], [Entry("v4", T0, null, null, LibraryListLabels.Playing)]);
        setup.Service.Choices = new VndbSyncChoices { MarkFinishedOnVndb = true };
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
    public async Task Marcar_como_terminado_sem_token_fica_so_aqui()
    {
        using var setup = new Setup([Game("g1", "v4")], [], withToken: false);
        setup.Service.Choices = new VndbSyncChoices { MarkFinishedOnVndb = true };

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

    [Fact]
    public async Task A_escolha_campo_a_campo_do_dialogo_vale_mesmo_contra_o_lado_mais_recente()
    {
        var local = new LibraryListState("v4", new LibraryListFields { Vote = 90 }, T0.AddHours(1))
        {
            Base = new LibraryListFields { Vote = 80 },
            SyncedAt = T0,
            OnVndb = true,
        };
        using var setup = new Setup([Game("g1", "v4") with { List = local }], [Entry("v4", T0.AddHours(2), 70, null)]);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));

        await connection.SyncNowCommand.ExecuteAsync(null);
        connection.Conflicts.Single().UseLocal = true;
        await connection.ApplyConflictsCommand.ExecuteAsync(null);

        Assert.Equal(90, setup.Local("g1").Vote);
        Assert.Equal(90, (await setup.Remote("v4")).Vote);
    }

    [Fact]
    public async Task As_escolhas_da_sincronizacao_ficam_na_pasta_de_configuracao_e_a_sincronizacao_as_respeita()
    {
        using var setup = new Setup([Game("g1", "v4")], [Entry("v4", T0, 80, "Do site", LibraryListLabels.Playing)], persistChoices: true);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));

        connection.SyncVote = false;
        connection.MarkFinishedOnVndb = true;
        Assert.True(connection.SyncLabels);
        Assert.True(connection.SyncDates);
        Assert.True(connection.SyncNotes);
        Assert.True(connection.MarkFinishedOnVndb);
        Assert.False(connection.SyncVote);

        var saved = new VndbSyncSettingsFile(setup.Config).Load();
        Assert.False(saved.Vote);
        Assert.True(saved.MarkFinishedOnVndb);
        Assert.True(saved.Notes);
        Assert.Equal(setup.Config, Path.GetDirectoryName(new VndbSyncSettingsFile(setup.Config).Path));

        await connection.SyncNowCommand.ExecuteAsync(null);

        var fields = setup.Local("g1");
        Assert.Null(fields.Vote);
        Assert.Equal("Do site", fields.Notes);
        Assert.True(fields.HasLabel(LibraryListLabels.Playing));
    }

    [Fact]
    public async Task Terminar_aqui_so_sobe_ao_VNDB_quando_a_escolha_esta_ligada()
    {
        using var setup = new Setup([Game("g1", "v4")], []);

        await setup.Service.EditAsync("g1", new VndbListUpdate { LabelsSet = [LibraryListLabels.Finished] }, Ct);
        Assert.DoesNotContain(setup.Fake.Calls, c => c.StartsWith(nameof(IVndbUserList.UpdateEntryAsync), StringComparison.Ordinal));
        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Finished));

        setup.Service.Choices = new VndbSyncChoices { MarkFinishedOnVndb = true };
        await setup.Service.EditAsync("g1", new VndbListUpdate { LabelsSet = [LibraryListLabels.Finished], Vote = 90 }, Ct);

        Assert.Contains(setup.Fake.Calls, c => c.StartsWith(nameof(IVndbUserList.UpdateEntryAsync), StringComparison.Ordinal));
        Assert.Contains((await setup.Remote("v4")).Labels, l => l.Id == LibraryListLabels.Finished);
    }

    [Fact]
    public async Task O_cartao_da_Ficha_mostra_a_lista_e_edita_rotulo_voto_datas_e_notas()
    {
        using var setup = new Setup([Game("g1", "v4")], []);
        setup.Service.Edit("g1", new VndbListUpdate { LabelsSet = [LibraryListLabels.Playing], Vote = 80, Started = new DateOnly(2026, 10, 3), Notes = "Voltar na rota da Aoi." });
        var library = new LibraryViewModel(setup.File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Later)) { ListService = setup.Service };
        await library.LoadAsync();

        var card = library.ListCardForProject(null, "v4")!;
        Assert.Equal("8", card.VoteInput);
        Assert.Equal("03/10/2026", card.StartedInput);
        Assert.Equal(string.Empty, card.FinishedInput);
        Assert.Equal("Mudança só aqui", card.SyncText);
        Assert.Equal(["Jogando"], card.Labels.Where(l => l.IsOn).Select(l => l.Text));

        await card.ToggleLabelCommand.ExecuteAsync(card.Labels.Single(l => l.Text == "Terminado"));
        Assert.True(setup.Local("g1").HasLabel(LibraryListLabels.Finished));
        Assert.Equal(["Jogando", "Terminado"], card.Labels.Where(l => l.IsOn).Select(l => l.Text));

        card.VoteInput = "8,5";
        card.FinishedInput = "07/11/2026";
        card.NotesText = "Final da Aoi.";
        await card.CommitCommand.ExecuteAsync(null);
        var saved = setup.Local("g1");
        Assert.Equal(85, saved.Vote);
        Assert.Equal(new DateOnly(2026, 11, 7), saved.Finished);
        Assert.Equal("Final da Aoi.", saved.Notes);

        card.FinishedInput = "ontem";
        await card.CommitCommand.ExecuteAsync(null);
        Assert.Contains("03/10/2026", card.Message);
        Assert.Equal(new DateOnly(2026, 11, 7), setup.Local("g1").Finished);

        card.FinishedInput = string.Empty;
        card.VoteInput = string.Empty;
        await card.CommitCommand.ExecuteAsync(null);
        Assert.Null(setup.Local("g1").Finished);
        Assert.Null(setup.Local("g1").Vote);
    }

    [Fact]
    public async Task O_cartao_segue_o_jogo_ligado_a_pasta_do_projeto_e_some_sem_confirmacao()
    {
        using var setup = new Setup([Game("g1", "v4") with { ProjectPath = "C:\\Projetos\\um" }, Game("g2", "v4") with { ProjectPath = "C:\\Projetos\\dois" }, Game("g3", "v9") with { VndbId = null, Confirmation = LibraryConfirmation.None }], []);
        var library = new LibraryViewModel(setup.File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Later)) { ListService = setup.Service };
        await library.LoadAsync();

        await library.ListCardForProject("c:\\projetos\\dois", "v4")!.ToggleLabelCommand.ExecuteAsync(new VndbLabelChip(LibraryListLabels.Playing, "Jogando", false));

        Assert.True(setup.Local("g2").HasLabel(LibraryListLabels.Playing));
        Assert.Null(setup.File.Load().Find("g1").CurrentList);
        Assert.Null(library.ListCardForProject(null, null));
    }

    [Fact]
    public async Task Interruptor_com_a_pasta_travada_mostra_o_erro_e_nao_derruba()
    {
        using var setup = new Setup([Game("g1", "v4")], []);
        var blocked = Path.Combine(setup.Config, "ocupado");
        await File.WriteAllTextAsync(blocked, "x", Ct);
        var service = new VndbListService(setup.Store, setup.Fake, setup.File, new FixedClock(Later), _ => null, new VndbSyncSettingsFile(blocked));
        var connection = new VndbConnectionViewModel(service, new FixedClock(Later));

        connection.SyncVote = false;

        Assert.Contains("não foi guardada", connection.Message);
        Assert.False(connection.SyncVote);
    }

    [Fact]
    public async Task Library_json_ilegivel_nao_derruba_a_aba_VNDB()
    {
        using var setup = new Setup([Game("g1", "v4")], []);
        await File.WriteAllTextAsync(setup.File.Path, "isto não é json", Ct);

        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));

        Assert.NotEmpty(connection.Message ?? string.Empty);
        Assert.Equal(string.Empty, connection.LastSyncText);
    }

    [Fact]
    public async Task Notas_com_espaco_no_fim_nao_contam_como_mudanca()
    {
        var state = new LibraryListState("v4", new LibraryListFields { Notes = "Voltar na rota da Aoi.\n " }, T0) { Base = new LibraryListFields { Notes = "Voltar na rota da Aoi.\n " }, SyncedAt = T0, OnVndb = true };
        using var setup = new Setup([Game("g1", "v4") with { List = state }], []);
        var library = new LibraryViewModel(setup.File, new FakeFolderPicker(null), action => action(), clock: new FixedClock(Later)) { ListService = setup.Service };
        await library.LoadAsync();
        var card = library.ListCardForProject(null, "v4")!;

        await card.CommitCommand.ExecuteAsync(null);

        Assert.True(LibraryListState.SameAs(state, setup.File.Load().Find("g1").CurrentList));
        Assert.StartsWith("Sincronizada", card.SyncText);
    }

    [Fact]
    public async Task Sem_token_guardado_colar_vazio_e_sincronizar_so_pedem_o_token()
    {
        using var setup = new Setup([Game("g1", "v4")], [], withToken: false);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));

        connection.ChangeTokenCommand.Execute(null);
        Assert.True(connection.IsChangingToken);
        connection.NewToken = "   ";
        await connection.SaveTokenCommand.ExecuteAsync(null);
        Assert.Equal("Cole o token que você gerou em vndb.org/u/tokens.", connection.Message);
        Assert.Null(setup.Store.Load());

        connection.NewToken = "meio-colado";
        connection.CancelChangeTokenCommand.Execute(null);
        Assert.False(connection.IsChangingToken);
        Assert.Equal(string.Empty, connection.NewToken);

        await connection.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal("Guarde o token do VNDB antes de sincronizar.", connection.Message);
    }

    [Fact]
    public async Task Cada_campo_da_sincronizacao_e_gravado_nas_escolhas()
    {
        using var setup = new Setup([Game("g1", "v4")], []);
        var connection = new VndbConnectionViewModel(setup.Service, new FixedClock(Later));
        await connection.RefreshAsync();

        connection.SyncLabels = false;
        connection.SyncDates = false;
        connection.SyncNotes = false;

        Assert.False(connection.SyncLabels);
        Assert.False(connection.SyncDates);
        Assert.False(connection.SyncNotes);
        Assert.False(setup.Service.Choices.Labels);
        Assert.False(setup.Service.Choices.Dates);
        Assert.False(setup.Service.Choices.Notes);
    }

    [Fact]
    public void Escolher_o_lado_remoto_de_um_conflito_desmarca_o_local_e_o_contrario()
    {
        var conflito = new VndbConflictRow("g1", "Jogo", "Notas", "local", "remoto", localWins: true);

        conflito.UseRemote = true;
        Assert.False(conflito.UseLocal);
        conflito.UseRemote = false;
        Assert.True(conflito.UseLocal);
    }
}
