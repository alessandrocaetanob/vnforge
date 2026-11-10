using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.Core.Tests.Library;

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
    public async Task Primeira_sincronizacao_traz_a_lista_do_VNDB_sem_o_rotulo_Voted()
    {
        var (library, fake) = Setup(Entry(lastModified: Later, vote: 85, notes: "Final bom", started: new DateOnly(2026, 1, 2), finished: new DateOnly(2026, 2, 3), labels: [2, 7]));

        var report = await Sync(library, fake);

        var item = Assert.Single(report.Items);
        Assert.Equal(LibrarySyncAction.Pulled, item.Action);
        var state = library.Games[0].CurrentList!;
        Assert.Equal(85, state.Fields.Vote);
        Assert.Equal("Final bom", state.Fields.Notes);
        Assert.Equal(new DateOnly(2026, 1, 2), state.Fields.Started);
        Assert.Equal(new DateOnly(2026, 2, 3), state.Fields.Finished);
        Assert.Equal([2], state.Fields.Labels.Select(l => l.Id));
        Assert.Equal(Later, state.ModifiedAt);
        Assert.False(state.Pending);
        Assert.Equal(Latest, state.SyncedAt);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mudanca_feita_aqui_sobe_e_a_proxima_sincronizacao_nao_faz_nada()
    {
        var (library, fake) = Setup(null);
        var game = library.Games[0];
        library.Replace(game with { List = LibraryListState.Edit(null, "v4", Edit(vote: 90, set: [2], notes: "Zerei"), T0) });

        var first = await Sync(library, fake);
        var second = await Sync(library, fake);

        Assert.Equal(LibrarySyncAction.Pushed, first.Items[0].Action);
        Assert.Equal(["rótulos", "voto", "notas"], first.Items[0].Pushed);
        var remote = (await Remote(fake))!;
        Assert.Equal(90, remote.Vote);
        Assert.Equal("Zerei", remote.Notes);
        Assert.Contains(remote.Labels, l => l.Id == 2);
        Assert.False(library.Games[0].CurrentList!.Pending);
        Assert.Equal(LibrarySyncAction.Unchanged, second.Items[0].Action);
        Assert.Single(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Campos_mudados_em_lados_diferentes_se_juntam_sem_conflito()
    {
        var agreed = Fields(vote: 70, notes: null, 1);
        var library = Synced(agreed with { Vote = 95 }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Latest, vote: 70, notes: "Nota do site", labels: [1, 7])]);

        var report = await Sync(library, fake);

        var item = Assert.Single(report.Items);
        Assert.Equal(LibrarySyncAction.Merged, item.Action);
        Assert.Empty(item.Conflicts);
        Assert.Equal(["notas"], item.Pulled);
        Assert.Equal(["voto"], item.Pushed);
        Assert.Equal("Nota do site", library.Games[0].CurrentList!.Fields.Notes);
        Assert.Equal(95, (await Remote(fake))!.Vote);
    }

    [Fact]
    public async Task Conflito_no_mesmo_campo_o_mais_recente_vence_e_avisa_quando_e_o_app()
    {
        var agreed = Fields(vote: 70);
        var library = Synced(agreed with { Vote = 95 }, Latest, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Later, vote: 60, labels: [7])]);

        var report = await Sync(library, fake);

        var conflict = Assert.Single(report.Items[0].Conflicts);
        Assert.Equal("voto", conflict.Field);
        Assert.Equal("9,5", conflict.Local);
        Assert.Equal("6", conflict.Remote);
        Assert.Equal(LibrarySyncWinner.Local, conflict.Winner);
        Assert.Equal(95, (await Remote(fake))!.Vote);
        Assert.Equal(95, library.Games[0].CurrentList!.Fields.Vote);
    }

    [Fact]
    public async Task Conflito_no_mesmo_campo_o_mais_recente_vence_e_avisa_quando_e_o_VNDB()
    {
        var agreed = Fields(vote: 70);
        var library = Synced(agreed with { Vote = 95 }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Latest, vote: 60, labels: [7])]);

        var report = await Sync(library, fake);

        var conflict = Assert.Single(report.Items[0].Conflicts);
        Assert.Equal(LibrarySyncWinner.Remote, conflict.Winner);
        Assert.Equal(60, library.Games[0].CurrentList!.Fields.Vote);
        Assert.Equal(60, (await Remote(fake))!.Vote);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pull_nao_envia_nada_e_a_mudanca_daqui_continua_pendente()
    {
        var agreed = Fields(vote: 70, notes: null);
        var library = Synced(agreed with { Vote = 95 }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Latest, vote: 70, notes: "Do site", labels: [7])]);

        var report = await Sync(library, fake, LibrarySyncDirection.Pull);

        Assert.Equal(LibrarySyncAction.Pulled, report.Items[0].Action);
        var state = library.Games[0].CurrentList!;
        Assert.Equal("Do site", state.Fields.Notes);
        Assert.Equal(95, state.Fields.Vote);
        Assert.True(state.Pending);
        Assert.Equal(70, state.Base!.Vote);
        Assert.Equal(70, (await Remote(fake))!.Vote);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Push_nao_traz_nada_e_a_mudanca_do_site_continua_valendo_la()
    {
        var agreed = Fields(vote: 70, notes: null);
        var library = Synced(agreed with { Vote = 95 }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Latest, vote: 70, notes: "Do site", labels: [7])]);

        var report = await Sync(library, fake, LibrarySyncDirection.Push);

        Assert.Equal(LibrarySyncAction.Pushed, report.Items[0].Action);
        var state = library.Games[0].CurrentList!;
        Assert.Null(state.Fields.Notes);
        Assert.Equal("Do site", (await Remote(fake))!.Notes);
        Assert.Equal(95, (await Remote(fake))!.Vote);
        Assert.Equal(95, state.Base!.Vote);
        Assert.Null(state.Base.Notes);
    }

    [Fact]
    public async Task Com_sentido_escolhido_ele_vence_os_conflitos_mesmo_sendo_o_mais_antigo()
    {
        var agreed = Fields(vote: 70);
        var pull = Synced(agreed with { Vote = 95 }, Latest, agreed);
        var push = Synced(agreed with { Vote = 95 }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Later, vote: 60, labels: [7])]);

        var pulled = await Sync(pull, fake, LibrarySyncDirection.Pull);
        var pushed = await Sync(push, fake, LibrarySyncDirection.Push);

        Assert.Equal(LibrarySyncWinner.Remote, Assert.Single(pulled.Items[0].Conflicts).Winner);
        Assert.Equal(60, pull.Games[0].CurrentList!.Fields.Vote);
        Assert.Equal(LibrarySyncWinner.Local, Assert.Single(pushed.Items[0].Conflicts).Winner);
        Assert.Equal(95, (await Remote(fake))!.Vote);
    }

    [Fact]
    public async Task A_escolha_da_pessoa_em_cada_conflito_vale_mais_que_o_lado_mais_recente()
    {
        var agreed = Fields(vote: 70, notes: "daqui");
        var library = Synced(agreed with { Vote = 95, Notes = "do app" }, Latest, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Later, vote: 60, notes: "do site", labels: [7])]);
        var options = new LibrarySyncOptions
        {
            Resolutions = new Dictionary<(string, string), LibrarySyncWinner> { [("g1", "voto")] = LibrarySyncWinner.Remote, [("g1", "notas")] = LibrarySyncWinner.Local },
        };

        var synchronizer = new LibraryListSynchronizer(fake, new FakeTimeProvider(Latest));
        var plan = await synchronizer.PlanAsync(library, FakeVndbCatalog.ValidToken, options, Ct);
        await synchronizer.ApplyAsync(plan, FakeVndbCatalog.ValidToken, (game, state) => LibraryListSynchronizer.Keep(library, game, state), null, Ct);

        Assert.Equal([LibrarySyncWinner.Remote, LibrarySyncWinner.Local], plan.Items[0].Conflicts.Select(c => c.Winner));
        Assert.Equal(60, library.Games[0].CurrentList!.Fields.Vote);
        Assert.Equal("do app", library.Games[0].CurrentList!.Fields.Notes);
        Assert.Equal("do app", (await Remote(fake))!.Notes);
        Assert.Equal(60, (await Remote(fake))!.Vote);
    }

    [Fact]
    public async Task Campo_de_fora_da_sincronizacao_nao_e_lido_nem_enviado()
    {
        var library = Synced(Fields(vote: 70, notes: "daqui"), Latest);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Later, vote: 60, notes: "do site", labels: [7])]);
        var local = library.Games[0].CurrentList!.Fields with { Vote = 95, Notes = "do app" };
        library.Replace(library.Games[0] with { List = library.Games[0].CurrentList! with { Fields = local } });

        var synchronizer = new LibraryListSynchronizer(fake, new FakeTimeProvider(Latest));
        var plan = await synchronizer.PlanAsync(library, FakeVndbCatalog.ValidToken, new LibrarySyncOptions { Fields = LibrarySyncFields.Notes }, Ct);
        await synchronizer.ApplyAsync(plan, FakeVndbCatalog.ValidToken, (game, state) => LibraryListSynchronizer.Keep(library, game, state), null, Ct);

        Assert.Equal(60, (await Remote(fake))!.Vote);
        Assert.Equal(95, library.Games[0].CurrentList!.Fields.Vote);
        Assert.Equal("do app", (await Remote(fake))!.Notes);
        Assert.Equal(["notas"], plan.Items[0].Pushed);
    }

    [Fact]
    public async Task Rotulos_de_um_lado_so_passam_para_o_outro_e_na_primeira_vez_se_somam()
    {
        var agreed = Fields(vote: null, notes: null, 1, 3);
        var library = Synced(agreed with { Labels = [LibraryListLabels.Named(1), LibraryListLabels.Named(3), LibraryListLabels.Named(5)] }, Later, agreed);
        var fake = new FakeVndbCatalog(list: [Entry(lastModified: Latest, labels: [3, 4])]);

        await Sync(library, fake);

        Assert.Equal([3, 4, 5], library.Games[0].CurrentList!.Fields.Labels.Select(l => l.Id));
        Assert.Equal([3, 4, 5], (await Remote(fake))!.Labels.Select(l => l.Id).Order());

        var first = new GameLibrary([Game() with { List = new LibraryListState("v4", Fields(null, null, 2), Later) }]);
        var other = new FakeVndbCatalog(list: [Entry(lastModified: Latest, labels: [1])]);

        await Sync(first, other);

        Assert.Equal([1, 2], first.Games[0].CurrentList!.Fields.Labels.Select(l => l.Id));
    }

    [Fact]
    public async Task Jogo_sem_estado_local_nunca_e_criado_na_lista_do_VNDB()
    {
        var (library, fake) = Setup(null);

        var both = await Sync(library, fake);
        var push = await Sync(library, fake, LibrarySyncDirection.Push);

        Assert.All(both.Items.Concat(push.Items), i => Assert.Equal(LibrarySyncAction.Unchanged, i.Action));
        Assert.Null(library.Games[0].List);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Push_nao_cria_estado_para_o_que_so_existe_no_VNDB()
    {
        var (library, fake) = Setup(Entry(vote: 80, labels: [2]));

        var report = await Sync(library, fake, LibrarySyncDirection.Push);

        Assert.Equal(LibrarySyncAction.Unchanged, report.Items[0].Action);
        Assert.Null(library.Games[0].List);
    }

    [Fact]
    public async Task So_os_jogos_confirmados_entram_e_as_entradas_sem_jogo_na_biblioteca_sao_contadas()
    {
        var library = new GameLibrary([Game("g1", "v4"), Game("g2", null)]);
        var fake = new FakeVndbCatalog(list: [Entry("v4", vote: 80, labels: [2]), Entry("v99", vote: 50, labels: [1])]);

        var report = await Sync(library, fake);

        Assert.Equal("g1", Assert.Single(report.Items).Game.Id);
        Assert.Equal(1, report.RemoteOnly);
        Assert.Null(library.Games[1].List);
    }

    [Fact]
    public async Task Plano_nao_grava_nada_nem_no_VNDB_nem_na_biblioteca()
    {
        var (library, fake) = Setup(null);
        library.Replace(library.Games[0] with { List = LibraryListState.Edit(null, "v4", Edit(vote: 80), T0) });
        var before = library.Games[0];

        var plan = await new LibraryListSynchronizer(fake).PlanAsync(library, FakeVndbCatalog.ValidToken, new LibrarySyncOptions(), Ct);

        Assert.Equal(1, plan.PushCount);
        Assert.Equal(["voto"], plan.Items[0].Pushed);
        Assert.Same(before, library.Games[0]);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
        Assert.Equal("u1", plan.Account.UserId);
    }

    [Fact]
    public async Task Entrada_apagada_no_VNDB_apaga_o_estado_local_que_nao_foi_mexido()
    {
        var fields = Fields(vote: 80, notes: null, 2);
        var library = Synced(fields, T0, fields);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake);

        Assert.Equal(LibrarySyncAction.Pulled, report.Items[0].Action);
        Assert.True(report.Items[0].RemovedOnSite);
        Assert.Null(library.Games[0].CurrentList!.Fields.Vote);
        Assert.Empty(library.Games[0].CurrentList!.Fields.Labels);
        Assert.False(library.Games[0].CurrentList!.OnVndb);
    }

    [Theory]
    [InlineData(LibrarySyncDirection.Both)]
    [InlineData(LibrarySyncDirection.Push)]
    [InlineData(LibrarySyncDirection.Pull)]
    public async Task Entrada_apagada_no_site_com_edicao_local_nao_e_recriada_e_aparece_no_plano(LibrarySyncDirection direction)
    {
        var agreed = Fields(vote: 70);
        var library = Synced(agreed with { Vote = 95 }, Latest, agreed);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake, direction);

        var item = Assert.Single(report.Items);
        Assert.True(item.RemovedOnSite);
        Assert.Equal(LibrarySyncAction.Unchanged, item.Action);
        Assert.Empty(item.Pushed);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
        Assert.Empty((await fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items);
        Assert.Equal(95, library.Games[0].CurrentList!.Fields.Vote);
        Assert.True(library.Games[0].CurrentList!.Pending);
    }

    [Fact]
    public async Task Entrada_apagada_no_site_so_e_recriada_com_o_pedido_e_com_tudo_o_que_o_app_guarda()
    {
        var agreed = Fields(vote: 70, notes: "Antes", 2);
        var library = Synced(agreed with { Vote = 95 }, Latest, agreed);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake, recreate: true);

        Assert.Equal(LibrarySyncAction.Pushed, report.Items[0].Action);
        var remote = (await Remote(fake))!;
        Assert.Equal(95, remote.Vote);
        Assert.Equal("Antes", remote.Notes);
        Assert.Contains(remote.Labels, l => l.Id == 2);
        Assert.True(library.Games[0].CurrentList!.OnVndb);
        Assert.False(library.Games[0].CurrentList!.Pending);
    }

    [Fact]
    public async Task Push_depois_da_exclusao_no_site_nao_a_consome_e_a_edicao_seguinte_avisa_em_vez_de_recriar()
    {
        var fields = Fields(vote: 70, notes: null, 2);
        var library = Synced(fields, T0, fields);
        var fake = new FakeVndbCatalog();

        var pushed = await Sync(library, fake, LibrarySyncDirection.Push);

        Assert.True(pushed.Items[0].RemovedOnSite);
        Assert.False(pushed.Items[0].HasLocalEdits);
        Assert.Equal(LibrarySyncAction.Unchanged, pushed.Items[0].Action);
        Assert.True(library.Games[0].CurrentList!.OnVndb);
        Assert.Equal(70, library.Games[0].CurrentList!.Fields.Vote);

        library.Replace(library.Games[0] with { List = LibraryListState.Edit(library.Games[0].CurrentList, "v4", Edit(vote: 95), Later) });
        var both = await Sync(library, fake);

        Assert.True(both.Items[0].RemovedOnSite);
        Assert.True(both.Items[0].HasLocalEdits);
        Assert.Equal(LibrarySyncAction.Unchanged, both.Items[0].Action);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
        Assert.Empty((await fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items);
        Assert.Equal(95, library.Games[0].CurrentList!.Fields.Vote);
    }

    [Theory]
    [InlineData(LibrarySyncDirection.Both)]
    [InlineData(LibrarySyncDirection.Pull)]
    [InlineData(LibrarySyncDirection.Push)]
    public async Task Recriar_sem_edicao_local_nao_recria_e_so_o_sentido_que_traz_consome_a_exclusao(LibrarySyncDirection direction)
    {
        var fields = Fields(vote: 70, notes: null, 2);
        var library = Synced(fields, T0, fields);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake, direction, recreate: true);

        Assert.True(report.Items[0].RemovedOnSite);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
        Assert.Empty((await fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items);
        var state = library.Games[0].CurrentList!;
        Assert.False(report.Items[0].HasLocalEdits);
        if (direction != LibrarySyncDirection.Push)
        {
            Assert.Equal(LibrarySyncAction.Pulled, report.Items[0].Action);
            Assert.Null(state.Fields.Vote);
            Assert.False(state.OnVndb);
        }
        else
        {
            Assert.Equal(LibrarySyncAction.Unchanged, report.Items[0].Action);
            Assert.Equal(70, state.Fields.Vote);
            Assert.True(state.OnVndb);
        }
    }

    [Fact]
    public async Task Recriar_com_dois_jogos_apagados_recria_so_o_que_tem_edicao()
    {
        var fields = Fields(vote: 70);
        var edited = Game("g1", "v4") with { List = new LibraryListState("v4", fields with { Vote = 95 }, Later) { Base = fields, SyncedAt = T0, OnVndb = true } };
        var untouched = Game("g2", "v17") with { List = new LibraryListState("v17", fields, T0) { Base = fields, SyncedAt = T0, OnVndb = true } };
        var library = new GameLibrary([edited, untouched]);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake, recreate: true);

        Assert.Equal([LibrarySyncAction.Pushed, LibrarySyncAction.Pulled], report.Items.Select(i => i.Action));
        Assert.Equal(["v4"], (await fake.GetListAsync("u1", FakeVndbCatalog.ValidToken, Ct)).Items.Select(e => e.VisualNovelId));
        Assert.Null(library.Games[1].CurrentList!.Fields.Vote);
    }

    [Fact]
    public async Task Recriar_nao_vale_com_pull()
    {
        var agreed = Fields(vote: 70);
        var library = Synced(agreed with { Vote = 95 }, Latest, agreed);
        var fake = new FakeVndbCatalog();

        var report = await Sync(library, fake, LibrarySyncDirection.Pull, recreate: true);

        Assert.True(report.Items[0].RemovedOnSite);
        Assert.Equal(LibrarySyncAction.Unchanged, report.Items[0].Action);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("UpdateEntryAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Estado_que_nunca_chegou_ao_VNDB_nao_conta_como_apagado_no_site()
    {
        var (library, fake) = Setup(null);
        library.Replace(library.Games[0] with { List = LibraryListState.Edit(null, "v4", Edit(vote: 80), T0) });

        var report = await Sync(library, fake);

        Assert.False(report.Items[0].RemovedOnSite);
        Assert.Equal(LibrarySyncAction.Pushed, report.Items[0].Action);
    }

    [Fact]
    public async Task Gravar_relendo_o_arquivo_nao_perde_o_que_outro_processo_gravou_durante_a_espera()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vnforge-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = new LibraryFile(dir);
            var other = new LibraryFile(dir);
            file.Save(new GameLibrary([Game("g1", "v4"), Game("g2", "v17")]));
            file.Update(l => l.Replace(l.Games[0] with { List = LibraryListState.Edit(null, "v4", Edit(vote: 80), T0) }));
            var library = file.Load();
            var fake = new FakeVndbCatalog();
            var writing = new DuringUpdate(fake, () => other.Update(l =>
            {
                l.Privacy = new LibraryPrivacy(ShowAdult: true);
                l.Replace(l.Games[1] with { Title = "Mudou fora", ProjectPath = "C:\\Projetos\\g2" });
            }));
            var synchronizer = new LibraryListSynchronizer(writing, new FakeTimeProvider(Latest));
            var plan = await synchronizer.PlanAsync(library, FakeVndbCatalog.ValidToken, new LibrarySyncOptions(), Ct);

            var report = await synchronizer.ApplyAsync(plan, FakeVndbCatalog.ValidToken, (game, state) => file.Update(current => LibraryListSynchronizer.Keep(current, game, state)), null, Ct);

            Assert.Equal(LibrarySyncAction.Pushed, report.Items[0].Action);
            var saved = file.Load();
            Assert.True(saved.Privacy.ShowAdult);
            Assert.Equal("Mudou fora", saved.Games[1].Title);
            Assert.Equal("C:\\Projetos\\g2", saved.Games[1].ProjectPath);
            Assert.False(saved.Games[0].CurrentList!.Pending);
            Assert.True(saved.Games[0].CurrentList!.OnVndb);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Jogo_editado_por_outro_lado_durante_a_espera_nao_e_sobrescrito()
    {
        var (library, fake) = Setup(null);
        library.Replace(library.Games[0] with { List = LibraryListState.Edit(null, "v4", Edit(vote: 80), T0) });
        var edited = new DuringUpdate(fake, () => library.Replace(library.Games[0] with { List = LibraryListState.Edit(library.Games[0].CurrentList, "v4", Edit(notes: "Escrita no meio"), Latest) }));

        var report = await Sync(library, edited);

        Assert.Equal(LibrarySyncAction.Failed, report.Items[0].Action);
        Assert.Contains("Rode de novo", report.Items[0].Error, StringComparison.Ordinal);
        Assert.Equal("Escrita no meio", library.Games[0].CurrentList!.Fields.Notes);
        Assert.True(library.Games[0].CurrentList!.Pending);
    }

    [Fact]
    public async Task Estado_de_outro_jogo_do_VNDB_nao_vale()
    {
        var game = Game() with { List = new LibraryListState("v17", Fields(vote: 80), T0) };

        Assert.Null(game.CurrentList);
        var library = new GameLibrary([game]);
        var fake = new FakeVndbCatalog(list: [Entry(vote: 50)]);

        await Sync(library, fake);

        Assert.Equal("v4", library.Games[0].List!.VisualNovelId);
        Assert.Equal(50, library.Games[0].List!.Fields.Vote);
    }

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

    [Fact]
    public async Task Falha_em_um_jogo_nao_para_os_outros_e_o_estado_dele_nao_muda()
    {
        var library = new GameLibrary([Game("g1", "v4"), Game("g2", "v17")]);
        var fake = new FakeVndbCatalog();
        foreach (var (id, vn) in new[] { ("g1", "v4"), ("g2", "v17") })
        {
            library.Replace(library.Games.Single(g => g.Id == id) with { List = LibraryListState.Edit(null, vn, Edit(vote: 80), T0) });
        }

        var report = await Sync(library, new FailingUpdates(fake, ["v4"], 500));

        Assert.Equal([LibrarySyncAction.Failed, LibrarySyncAction.Pushed], report.Items.Select(i => i.Action));
        Assert.Null(report.AbortReason);
        Assert.NotNull(report.Items[0].Error);
        Assert.True(library.Games[0].CurrentList!.Pending);
        Assert.False(library.Games[1].CurrentList!.Pending);
    }

    [Fact]
    public async Task Resposta_401_na_gravacao_e_tres_falhas_seguidas_param_o_resto()
    {
        var unauthorized = new GameLibrary([Game("g1", "v4"), Game("g2", "v17")]);
        var overloaded = new GameLibrary([Game("g1", "v1"), Game("g2", "v2"), Game("g3", "v3"), Game("g4", "v4")]);
        foreach (var library in new[] { unauthorized, overloaded })
        {
            foreach (var game in library.Games.ToList())
            {
                library.Replace(game with { List = LibraryListState.Edit(null, game.VndbId!, Edit(vote: 80), T0) });
            }
        }

        var first = await Sync(unauthorized, new FailingUpdates(new FakeVndbCatalog(), ["v4", "v17"], 401));
        var second = await Sync(overloaded, new FailingUpdates(new FakeVndbCatalog(), ["v1", "v2", "v3", "v4"], 502));

        Assert.Single(first.Items);
        Assert.Contains("token", first.AbortReason, StringComparison.Ordinal);
        Assert.Equal(3, second.Items.Count);
        Assert.Contains("3 vezes", second.AbortReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelar_guarda_o_que_ja_foi_feito()
    {
        var library = new GameLibrary([Game("g1", "v4"), Game("g2", "v17")]);
        foreach (var game in library.Games.ToList())
        {
            library.Replace(game with { List = LibraryListState.Edit(null, game.VndbId!, Edit(vote: 80), T0) });
        }

        using var cancel = new CancellationTokenSource();
        var fake = new FakeVndbCatalog();
        var synchronizer = new LibraryListSynchronizer(fake);
        var plan = await synchronizer.PlanAsync(library, FakeVndbCatalog.ValidToken, new LibrarySyncOptions(), Ct);
        var progress = new CancelAfterFirst(cancel);

        var report = await synchronizer.ApplyAsync(plan, FakeVndbCatalog.ValidToken, (game, state) => LibraryListSynchronizer.Keep(library, game, state), progress, cancel.Token);

        Assert.True(report.Cancelled);
        Assert.False(library.Games[0].CurrentList!.Pending);
        Assert.True(library.Games[1].CurrentList!.Pending);
    }

    private sealed class DuringUpdate(FakeVndbCatalog inner, Action during) : IVndbUserList
    {
        public Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default) => inner.GetAuthInfoAsync(token, ct);

        public Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default) =>
            inner.GetListAsync(userId, token, ct);

        public async Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default)
        {
            await Task.Yield();
            during();
            await inner.UpdateEntryAsync(token, visualNovelId, update, ct).ConfigureAwait(false);
        }
    }

    private sealed class CancelAfterFirst(CancellationTokenSource cancel) : IProgress<LibrarySyncProgress>
    {
        public void Report(LibrarySyncProgress value)
        {
            if (value.Done == 1)
            {
                cancel.Cancel();
            }
        }
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

    private sealed class FailingUpdates(FakeVndbCatalog inner, string[] failing, int status) : IVndbUserList
    {
        public Task<VndbAuthInfo> GetAuthInfoAsync(string token, CancellationToken ct = default) => inner.GetAuthInfoAsync(token, ct);

        public Task<VndbResults<VndbListEntry>> GetListAsync(string userId, string? token = null, CancellationToken ct = default) =>
            inner.GetListAsync(userId, token, ct);

        public Task UpdateEntryAsync(string token, string visualNovelId, VndbListUpdate update, CancellationToken ct = default) =>
            failing.Contains(visualNovelId)
                ? throw new VndbException("falhou", status)
                : inner.UpdateEntryAsync(token, visualNovelId, update, ct);
    }
}
