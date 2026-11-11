using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
using VNForge.Core.Vndb;
using VNForge.Vndb;

namespace VNForge.App.Tests;

/// <summary>A tela da Biblioteca (#548) sem janela: carga, filtros, gaveta, casamento, varredura e privacidade, sobre pastas e <c>library.json</c> reais e um VNDB de mentira.</summary>
public sealed class LibraryViewModelTests : IDisposable
{
    private readonly TempFolder _config = new();
    private readonly TempFolder _games = new();

    public void Dispose()
    {
        _config.Dispose();
        _games.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FixedClock Clock => new(LibrarySamples.Now);

    private LibraryFile File => new(_config.Path);

    private static VndbVisualNovel Vn(string id, string title, string? alt = null) =>
        new(id, title, alt, [], [], "ja", 0, "2008-04-24", ["ja"], ["win"], new VndbImage("https://t.vndb.org/cv/" + id + ".jpg", null, 300, 400, 0, 0), 4, 3000, 10, null, []);

    private static FakeVndbCatalog Catalog() => new([Vn("v4", "CLANNAD", "クラナド"), Vn("v7", "Kōrin no Mori", "光輪の森")]);

    private LibraryViewModel Create(
        IFolderPicker? picker = null,
        bool withServices = true,
        Action<string>? openFolder = null,
        FakeVndbCatalog? catalog = null,
        Action<Action>? post = null)
    {
        LibraryScanService? scan = null;
        LibraryEditor? editor = null;
        if (withServices)
        {
            var vndb = catalog ?? Catalog();
            scan = new LibraryScanService(File, new LibraryScanner(new EngineRecognizer([])), () => new LibraryMatcher(vndb, Clock), () => [], Clock);
            editor = new LibraryEditor(File, () => new LibraryMatcher(vndb, Clock));
        }

        return new LibraryViewModel(File, picker ?? new FakeFolderPicker(null), post ?? (action => action()), scan, editor, Clock, LibrarySamples.Translation, null, new LibrarySamples.Sheets(), openFolder);
    }

    private async Task<LibraryViewModel> Loaded(Action<LibraryViewModel>? configure = null, bool withServices = true, Action<string>? openFolder = null)
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        var vm = Create(withServices: withServices, openFolder: openFolder);
        configure?.Invoke(vm);
        await vm.LoadAsync();
        return vm;
    }

    private static LibraryCardViewModel Card(LibraryViewModel vm, string title) => vm.Cards.Single(c => c.Title == title);

    [Fact]
    public async Task Carrega_os_jogos_em_ordem_com_os_contadores_dos_filtros_e_o_subtitulo()
    {
        var vm = await Loaded();

        Assert.Equal(LibraryMode.Games, vm.Mode);
        Assert.True(vm.HasGames);
        Assert.Equal(12, vm.Cards.Count);
        Assert.Equal("Tsukiyo no Hanabi", vm.Cards[0].Title);
        Assert.Equal(["Todos", "Em tradução", "Sem português", "Sem suporte", "Engine suportada"], vm.Filters.Select(f => f.Label));
        Assert.Equal([12, 2, 9, 5, 6], vm.Filters.Select(f => f.Count));
        Assert.Equal("12 jogos em 2 pastas · varredura há 2 horas", vm.Subtitle);
        Assert.Equal("12 jogos na Biblioteca", vm.StatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Sem_jogos_a_biblioteca_e_vazia()
    {
        var vm = Create();

        await vm.LoadAsync();

        Assert.Equal(LibraryMode.Empty, vm.Mode);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Cards);
    }

    [Fact]
    public async Task Arquivo_ilegivel_vira_aviso_e_nao_lista_vazia_muda()
    {
        Directory.CreateDirectory(_config.Path);
        await System.IO.File.WriteAllTextAsync(File.Path, "{ isto não é json", Ct);
        var vm = Create();

        await vm.LoadAsync();

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Equal(LibraryMode.Empty, vm.Mode);
    }

    [Fact]
    public async Task A_busca_olha_titulo_original_e_tag_e_o_filtro_estreita_a_grade()
    {
        var vm = await Loaded();

        vm.SearchText = "kuroneko";
        Assert.Equal(["Kuroneko Archive"], vm.Cards.Select(c => c.Title));
        Assert.True(vm.HasFilter);

        vm.SearchText = "光輪";
        Assert.Equal(["Kōrin no Mori"], vm.Cards.Select(c => c.Title));

        vm.SearchText = "zzzz";
        Assert.Empty(vm.Cards);
        Assert.True(vm.HasNoResults);

        vm.SearchText = string.Empty;
        vm.ToggleFilterCommand.Execute(vm.Filters[1]);
        Assert.Equal(["Amaoto no Hakoniwa", "Namiuchigiwa no Shiori"], vm.Cards.Select(c => c.Title).Order());
        Assert.True(vm.Filters[1].IsSelected);
        Assert.False(vm.Filters[0].IsSelected);
        Assert.Equal(LibraryFilter.InTranslation, vm.SelectedFilter);

        vm.SelectFilter(LibraryFilter.Unsupported);
        Assert.Equal(5, vm.Cards.Count);

        vm.ToggleFilterCommand.Execute(null);
        Assert.Equal(12, vm.Cards.Count);
        Assert.False(vm.HasFilter);
    }

    [Fact]
    public async Task Escolher_um_jogo_abre_a_gaveta_e_escolher_de_novo_fecha()
    {
        var vm = await Loaded();
        var card = Card(vm, "Amaoto no Hakoniwa");

        vm.ToggleCommand.Execute(card);

        Assert.Same(card, vm.Selected);
        Assert.True(card.IsSelected);
        Assert.True(vm.HasDrawer);
        Assert.Equal("Amaoto no Hakoniwa", vm.Drawer!.Title);

        var other = Card(vm, "Fuyu no Shiori");
        vm.ToggleCommand.Execute(other);
        Assert.False(card.IsSelected);
        Assert.True(other.IsSelected);

        vm.ToggleCommand.Execute(other);
        Assert.Null(vm.Selected);
        Assert.False(vm.HasDrawer);

        vm.ToggleCommand.Execute(card);
        vm.CloseDrawerCommand.Execute(null);
        Assert.False(vm.HasDrawer);
    }

    [Fact]
    public async Task A_escolha_sobrevive_a_uma_busca_que_ainda_mostra_o_jogo_e_cai_quando_ele_some()
    {
        var vm = await Loaded();
        vm.Select(Card(vm, "Amaoto no Hakoniwa"));

        vm.SearchText = "amaoto";
        Assert.NotNull(vm.Selected);

        vm.SearchText = "fuyu";
        Assert.Null(vm.Selected);
        Assert.False(vm.HasDrawer);
    }

    [Fact]
    public async Task A_gaveta_traz_as_tags_do_VNDB_o_mais_N_e_avisa_do_spoiler_escondido()
    {
        var vm = await Loaded();

        vm.Select(Card(vm, "Amaoto no Hakoniwa"));

        var chips = vm.Drawer!.Chips;
        Assert.Equal(["Escola", "Mistério", "Chuva"], chips.Where(c => c.Kind == LibraryDrawerChipKind.Vndb).Select(c => c.Text));
        Assert.Contains(chips, c => c is { Kind: LibraryDrawerChipKind.More, Text: "+6" });
        var hidden = Assert.Single(chips, c => c.IsHidden);
        Assert.Equal("Spoiler escondido", hidden.Text);
        Assert.DoesNotContain(chips, c => c.Text.Contains("trágico", StringComparison.Ordinal));
        Assert.All(chips.Where(c => c.Kind != LibraryDrawerChipKind.Hidden), c => Assert.True(c.IsPlain));
        Assert.True(vm.Drawer.HasMoreTags);
        Assert.True(vm.Drawer.ShowSpoilerMarker);
        Assert.False(vm.Drawer.ShowAdultMarker);
        Assert.Equal("+6", vm.Drawer.MoreTagsText);
        Assert.True(vm.Drawer.HasTags);
    }

    [Fact]
    public async Task Conteudo_adulto_fica_escondido_na_grade_e_na_gaveta_ate_a_pessoa_liberar()
    {
        var vm = await Loaded();

        var card = Card(vm, "Kuroneko Archive");
        Assert.True(card.IsAdultVeil);
        Assert.False(card.HasImage);
        Assert.Null(card.Image);

        vm.Select(card);
        Assert.False(vm.Drawer!.Card.HasImage);
        Assert.DoesNotContain(vm.Drawer.Chips, c => c.Text == "Final trágico");
    }

    [Fact]
    public async Task Jogo_sem_casamento_mostra_a_placa_sem_capa_e_o_aviso()
    {
        var vm = await Loaded();

        var toge = Card(vm, "Tōge no Kaji");

        Assert.True(toge.HasNoArt);
        Assert.True(toge.HasState);
        Assert.Equal("Engine não reconhecida", toge.Chip);
        Assert.True(Card(vm, "Kōrin no Mori").StateIsWarning);
        Assert.False(Card(vm, "Amaoto no Hakoniwa").StateIsWarning);
        Assert.Equal(0.38, Card(vm, "Amaoto no Hakoniwa").Progress, 2);
        Assert.False(toge.HasProgress);
    }

    [Fact]
    public async Task Abrir_jogo_avisa_o_shell_com_o_jogo_da_gaveta_ou_do_cartao()
    {
        var vm = await Loaded();
        var opened = new List<string>();
        vm.GameOpenRequested += (_, game) => opened.Add(game.Title);

        vm.OpenGameCommand.Execute(null);
        Assert.Empty(opened);

        vm.Select(Card(vm, "Amaoto no Hakoniwa"));
        vm.OpenGameCommand.Execute(null);
        vm.OpenGameCommand.Execute(Card(vm, "Fuyu no Shiori"));

        Assert.Equal(["Amaoto no Hakoniwa", "Fuyu no Shiori"], opened);
    }

    [Fact]
    public async Task Mostrar_a_pasta_chama_o_abridor_com_a_pasta_do_jogo()
    {
        var shown = new List<string>();
        var vm = await Loaded(openFolder: shown.Add);
        vm.Select(Card(vm, "Amaoto no Hakoniwa"));

        vm.ShowFolderCommand.Execute(null);

        Assert.Equal([vm.Selected!.Game.Folder], shown);
    }

    [Fact]
    public async Task A_biblioteca_vazia_pede_um_projeto_existente_e_a_lista_troca_de_aparencia()
    {
        var vm = await Loaded();
        var asked = 0;
        vm.ExistingProjectRequested += (_, _) => asked++;

        vm.OpenExistingProjectCommand.Execute(null);
        Assert.Equal(1, asked);

        Assert.True(vm.IsGrid);
        vm.SelectedLayout = vm.Layouts[1];
        Assert.True(vm.IsList);
        Assert.Equal(LibraryLayout.List, vm.Layout);
        vm.SelectedLayout = vm.Layouts[0];
        Assert.True(vm.IsGrid);
    }

    [Fact]
    public async Task Um_problema_de_fora_aparece_como_aviso_da_biblioteca()
    {
        var vm = await Loaded();

        vm.ReportProblem("O projeto não abriu.");

        Assert.Equal("O projeto não abriu.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Confirmar_o_candidato_liga_o_jogo_ao_VNDB_e_tira_o_aviso()
    {
        var vm = await Loaded();
        var korin = Card(vm, "Kōrin no Mori");
        vm.Select(korin);

        vm.ChooseMatchCommand.Execute(null);

        Assert.True(vm.Match.IsOpen);
        Assert.Equal(3, vm.Match.Choices.Count);
        Assert.Equal("v7", vm.Match.Selected!.VndbId);
        Assert.Equal("Qual é este jogo no VNDB?", vm.Match.Title);
        Assert.Contains("3 candidatos", vm.Match.Intro, StringComparison.Ordinal);

        vm.Match.SelectChoiceCommand.Execute(vm.Match.Choices[1]);
        Assert.Equal("v8", vm.Match.Selected!.VndbId);
        vm.Match.SelectChoiceCommand.Execute(vm.Match.Choices[0]);

        await vm.Match.ConfirmCommand.ExecuteAsync(null);

        Assert.False(vm.Match.IsOpen);
        Assert.Null(vm.Match.ErrorMessage);
        Assert.False(Card(vm, "Kōrin no Mori").StateIsWarning);
        Assert.True(File.Load().Games.Single(g => g.Title == "Kōrin no Mori").IsConfirmed);
        Assert.Equal("Kōrin no Mori", vm.Selected!.Title);
    }

    [Fact]
    public async Task Nenhum_deles_descarta_os_candidatos_e_Depois_fecha_sem_mudar_nada()
    {
        var vm = await Loaded();
        var korin = Card(vm, "Kōrin no Mori");

        vm.ChooseMatchCommand.Execute(korin);
        vm.Match.LaterCommand.Execute(null);
        Assert.False(vm.Match.IsOpen);
        Assert.NotEmpty(File.Load().Games.Single(g => g.Title == "Kōrin no Mori").Candidates);

        vm.ChooseMatchCommand.Execute(korin);
        await vm.Match.NoneOfThemCommand.ExecuteAsync(null);

        Assert.False(vm.Match.IsOpen);
        Assert.Empty(File.Load().Games.Single(g => g.Title == "Kōrin no Mori").Candidates);
    }

    [Fact]
    public async Task Buscar_no_VNDB_de_novo_reabre_o_dialogo_com_o_que_o_VNDB_achou()
    {
        var vm = await Loaded();
        vm.ChooseMatchCommand.Execute(Card(vm, "Kōrin no Mori"));

        await vm.Match.SearchCommand.ExecuteAsync(null);

        Assert.True(vm.Match.IsOpen);
        Assert.False(vm.Match.IsBusy);
        Assert.Null(vm.Match.ErrorMessage);
        Assert.NotNull(File.Load().Games.Single(g => g.Title == "Kōrin no Mori").SearchedAt);
    }

    [Fact]
    public async Task Confirmar_um_jogo_que_sumiu_da_biblioteca_vira_aviso_na_tela()
    {
        var vm = await Loaded();
        vm.ChooseMatchCommand.Execute(Card(vm, "Kōrin no Mori"));
        File.Update(library => library.Remove(vm.Match.Game!.Id));

        await vm.Match.ConfirmCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Buscar_de_novo_um_jogo_que_sumiu_da_biblioteca_vira_aviso_na_tela()
    {
        var vm = await Loaded();
        vm.ChooseMatchCommand.Execute(Card(vm, "Kōrin no Mori"));
        File.Update(library => library.Remove(vm.Match.Game!.Id));

        await vm.Match.SearchCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Desfazer_o_casamento_e_tirar_da_biblioteca_mexem_so_no_library_json()
    {
        var vm = await Loaded();
        var amaoto = Card(vm, "Amaoto no Hakoniwa");

        await vm.UnconfirmCommand.ExecuteAsync(amaoto);

        Assert.False(File.Load().Games.Single(g => GameLibrary.FolderName(g) == "amaoto_hakoniwa").IsConfirmed);

        var fuyu = Card(vm, "Fuyu no Shiori");
        vm.Select(fuyu);
        await vm.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(11, vm.Cards.Count);
        Assert.DoesNotContain(vm.Cards, c => c.Title == "Fuyu no Shiori");
        Assert.Null(vm.Selected);
        Assert.Equal(11, File.Load().Games.Count);
    }

    [Fact]
    public async Task Sem_os_servicos_de_varredura_e_edicao_os_comandos_nao_fazem_nada()
    {
        var vm = await Loaded(withServices: false);

        await vm.UnconfirmCommand.ExecuteAsync(Card(vm, "Amaoto no Hakoniwa"));
        await vm.RemoveCommand.ExecuteAsync(Card(vm, "Fuyu no Shiori"));
        await vm.RescanCommand.ExecuteAsync(null);
        vm.ChooseMatchCommand.Execute(Card(vm, "Kōrin no Mori"));
        await vm.Match.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(12, vm.Cards.Count);
        await vm.ScanFolderAsync(_games.Path);
        Assert.Equal("A varredura não está disponível.", vm.ErrorMessage);
    }

    private void Collection()
    {
        Touch("[Key] CLANNAD v1.02", "data.xp3");
        Touch("[Key] CLANNAD v1.02", "CLANNAD.exe");
        Touch("Steins;Gate", "Scene.pck");
        Touch("Steins;Gate", "Steins.exe");
        Touch("Sem Nome", "x.exe");
    }

    private void Touch(string folder, string file)
    {
        var path = Path.Combine(_games.Path, folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "x");
    }

    [Fact]
    public async Task A_primeira_varredura_acha_os_jogos_casa_com_o_VNDB_e_leva_a_biblioteca()
    {
        Collection();
        var vm = Create(new FakeFolderPicker(_games.Path));
        await vm.LoadAsync();
        Assert.True(vm.IsEmpty);

        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal(LibraryMode.FirstScan, vm.Mode);
        Assert.True(vm.IsFirstScan);
        Assert.Equal(LibraryScanPhase.Done, vm.Scan.Phase);
        Assert.True(vm.Scan.IsDone);
        Assert.Equal(_games.Path, vm.Scan.Folder.TrimEnd('\\'), ignoreCase: true);
        Assert.NotEmpty(vm.Scan.Rows);
        Assert.True(vm.Scan.NoSupportYet >= 1);
        Assert.True(vm.Scan.ShowTable);
        Assert.Equal("Ver a Biblioteca", vm.Scan.ActionText);
        Assert.Equal(string.Empty, vm.Scan.StatusText);
        Assert.Equal(3, File.Load().Games.Count);
        Assert.Single(File.Load().Roots);

        await vm.ScanActionCommand.ExecuteAsync(null);

        Assert.Equal(LibraryMode.Games, vm.Mode);
        Assert.Equal(LibraryScanPhase.Idle, vm.Scan.Phase);
        Assert.Equal(3, vm.Cards.Count);
        Assert.Contains("3 jogos em 1 pasta", vm.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Soltar_uma_pasta_faz_o_mesmo_que_escolher_e_varrer_de_novo_acha_o_jogo_novo()
    {
        Collection();
        var vm = Create();
        await vm.LoadAsync();

        await vm.DropFolderAsync(_games.Path);
        vm.FinishFirstScan();
        Assert.Equal(3, vm.Cards.Count);

        Touch("Novo Jogo", "data.xp3");
        Touch("Novo Jogo", "Novo.exe");
        await vm.RescanCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.Cards.Count);
        Assert.False(vm.IsRescanning);
        Assert.Equal(LibraryScanPhase.Idle, vm.Scan.Phase);
        Assert.Equal(LibraryMode.Games, vm.Mode);
    }

    [Fact]
    public async Task Ler_a_pasta_de_novo_ao_abrir_fica_guardado_e_vale_no_proximo_abrir()
    {
        Collection();
        var vm = Create();
        await vm.LoadAsync();
        await vm.ScanFolderAsync(_games.Path);

        vm.Scan.RescanOnOpen = true;
        await vm.Remembering;

        Assert.True(File.Load().Roots.Single().RescanOnOpen);

        Touch("Novo Jogo", "data.xp3");
        Touch("Novo Jogo", "Novo.exe");
        await vm.RescanOnOpenAsync();
        Assert.Equal(4, File.Load().Games.Count);
    }

    [Fact]
    public async Task Pasta_guardada_que_sumiu_avisa_em_vez_de_apagar_os_jogos()
    {
        var vm = await Loaded();

        await vm.RescanCommand.ExecuteAsync(null);

        Assert.Contains("Não deu para ler", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(12, File.Load().Games.Count);
        Assert.False(vm.IsRescanning);
    }

    [Fact]
    public async Task Varrer_de_novo_lista_todas_as_pastas_sem_acesso_no_fim_mesmo_com_pasta_boa_no_meio()
    {
        Collection();
        var missing1 = Path.Combine(_games.Path, "Sumiu 1");
        var missing2 = Path.Combine(_games.Path, "Sumiu 2");
        File.Update(l =>
        {
            l.SetRoot(new LibraryRoot(missing1));
            l.SetRoot(new LibraryRoot(_games.Path));
            l.SetRoot(new LibraryRoot(missing2));
        });
        var vm = Create();
        await vm.LoadAsync();

        await vm.RescanCommand.ExecuteAsync(null);

        Assert.Contains(GameLibrary.NormalizeFolder(missing1), vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(GameLibrary.NormalizeFolder(missing2), vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(3, File.Load().Games.Count);
    }

    [Fact]
    public async Task Pausar_durante_o_varrer_de_novo_para_o_laco_e_nao_varre_as_outras_pastas()
    {
        Collection();
        using var other = new TempFolder();
        System.IO.File.WriteAllText(Path.Combine(other.Path, "x.exe"), "x");
        LibraryViewModel? holder = null;
        var armed = false;
        var paused = false;
        var vm = Create(post: action =>
        {
            action();
            if (armed && !paused && holder!.Scan.Rows.Count > 0)
            {
                paused = true;
                holder.ScanActionCommand.Execute(null);
            }
        });
        holder = vm;
        await vm.LoadAsync();
        await vm.ScanFolderAsync(_games.Path);
        await vm.ScanFolderAsync(other.Path);
        vm.FinishFirstScan();
        Touch("Novo Jogo", "data.xp3");
        Touch("Novo Jogo", "Novo.exe");
        Directory.CreateDirectory(Path.Combine(other.Path, "Novo B"));
        System.IO.File.WriteAllText(Path.Combine(other.Path, "Novo B", "b.exe"), "b");

        armed = true;
        await vm.RescanCommand.ExecuteAsync(null);

        Assert.True(paused);
        Assert.False(vm.IsScanBusy);
        Assert.False(vm.IsRescanning);
        Assert.DoesNotContain(File.Load().Games, g => g.Folder.Contains("Novo B", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Um_erro_inesperado_ao_abrir_vira_aviso_da_biblioteca_e_nao_derruba_o_app()
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        var vm = new LibraryViewModel(File, new FakeFolderPicker(null), action => action(), clock: Clock, readTranslation: _ => throw new InvalidOperationException("leitura quebrada"));

        await vm.OpenAsync(() => Task.CompletedTask);

        Assert.Contains("leitura quebrada", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.IsScanBusy);
    }

    [Fact]
    public async Task Trocar_de_pasta_na_varredura_grava_o_ler_de_novo_ao_abrir_na_pasta_nova_e_nao_na_velha()
    {
        Collection();
        using var other = new TempFolder();
        var vm = Create();
        await vm.LoadAsync();
        await vm.ScanFolderAsync(_games.Path);
        File.Update(l => l.SetRoot(new LibraryRoot(other.Path, RescanOnOpen: true)));
        await vm.LoadAsync();

        await vm.ScanFolderAsync(other.Path);
        await vm.Remembering;

        var roots = File.Load().Roots;
        Assert.False(roots.Single(r => string.Equals(r.Path, GameLibrary.NormalizeFolder(_games.Path), StringComparison.OrdinalIgnoreCase)).RescanOnOpen);
        Assert.True(roots.Single(r => string.Equals(r.Path, GameLibrary.NormalizeFolder(other.Path), StringComparison.OrdinalIgnoreCase)).RescanOnOpen);
        Assert.True(vm.Scan.RescanOnOpen);
    }

    [Fact]
    public async Task Sem_varredura_rodando_a_acao_da_primeira_varredura_nao_faz_nada()
    {
        var vm = await Loaded();

        await vm.ScanActionCommand.ExecuteAsync(null);

        Assert.Equal(LibraryMode.Games, vm.Mode);
        Assert.Equal(LibraryScanPhase.Idle, vm.Scan.Phase);
    }

    [Fact]
    public async Task Ver_a_biblioteca_depois_da_varredura_ja_pede_o_primeiro_jogo_em_duvida()
    {
        var vm = await Loaded();
        vm.Mode = LibraryMode.FirstScan;
        vm.Scan.Begin("D:\\Jogos\\VN");

        vm.FinishFirstScan();

        Assert.Equal(LibraryMode.Games, vm.Mode);
        Assert.True(vm.Match.IsOpen);
        Assert.Equal("Kōrin no Mori", vm.Match.Game!.Title);
        Assert.True(vm.OpenFirstConfirmation());
    }

    [Fact]
    public async Task Sem_jogo_a_confirmar_o_dialogo_nao_abre()
    {
        var games = LibrarySamples.Games().Where(g => g.Title != "Kōrin no Mori").ToList();
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(games));
        var vm = Create();
        await vm.LoadAsync();

        Assert.False(vm.OpenFirstConfirmation());
        Assert.False(vm.Match.IsOpen);
    }

    [Fact]
    public async Task O_andamento_da_varredura_vira_o_texto_da_barra_de_status()
    {
        var vm = await Loaded();

        vm.Scan.Begin("D:\\Jogos\\VN");

        Assert.Equal("Varrendo D:\\Jogos\\VN", vm.StatusText);
        Assert.True(vm.Scan.IsRunning);
        Assert.Equal("Pausar", vm.Scan.ActionText);
        Assert.Equal("Varrendo", vm.Scan.Title);
    }

    [Fact]
    public async Task Pausar_e_continuar_a_primeira_varredura_segue_na_primeira_varredura()
    {
        Collection();
        LibraryViewModel? holder = null;
        var paused = false;
        var vm = Create(post: action =>
        {
            action();
            if (!paused && holder!.Scan.Rows.Count > 0)
            {
                paused = true;
                holder.ScanActionCommand.Execute(null);
            }
        });
        holder = vm;
        await vm.LoadAsync();

        await vm.ScanFolderAsync(_games.Path);

        Assert.Equal(LibraryScanPhase.Paused, vm.Scan.Phase);
        Assert.Equal("Continuar", vm.Scan.ActionText);
        Assert.Equal(LibraryMode.FirstScan, vm.Mode);

        await vm.ScanActionCommand.ExecuteAsync(null);

        Assert.Equal(LibraryMode.FirstScan, vm.Mode);
        Assert.Equal(LibraryScanPhase.Done, vm.Scan.Phase);
        Assert.True(vm.Scan.ShowTable);
        Assert.Equal(3, File.Load().Games.Count);
        Assert.Equal("Ver a Biblioteca", vm.Scan.ActionText);
    }

    [Fact]
    public async Task Trocar_a_pasta_no_meio_da_primeira_varredura_recomeca_na_primeira_varredura()
    {
        Collection();
        using var other = new TempFolder();
        System.IO.File.WriteAllText(Path.Combine(other.Path, "x.exe"), "x");
        LibraryViewModel? holder = null;
        var started = false;
        Task? change = null;
        var vm = Create(
            new FakeFolderPicker(other.Path),
            post: action =>
            {
                action();
                if (!started && holder!.Scan.Rows.Count > 0)
                {
                    started = true;
                    change = holder.ChangeFolderCommand.ExecuteAsync(null);
                }
            });
        holder = vm;
        await vm.LoadAsync();

        await vm.ScanFolderAsync(_games.Path);
        await change!;
        await vm.Scanning;

        Assert.Equal(LibraryMode.FirstScan, vm.Mode);
        Assert.Equal(LibraryScanPhase.Done, vm.Scan.Phase);
        Assert.Equal(other.Path, vm.Scan.Folder.TrimEnd('\\'), ignoreCase: true);
    }

    [Fact]
    public async Task Enquanto_uma_varredura_roda_adicionar_pasta_e_varrer_de_novo_esperam_e_a_pasta_pedida_entra_na_fila()
    {
        Collection();
        using var other = new TempFolder();
        LibraryViewModel? holder = null;
        var observed = false;
        var canChoose = true;
        var canRescan = true;
        Task? ignored = null;
        var vm = Create(
            new FakeFolderPicker(other.Path),
            post: action =>
            {
                action();
                if (!observed && holder!.Scan.Rows.Count > 0)
                {
                    observed = true;
                    canChoose = holder.ChooseFolderCommand.CanExecute(null);
                    canRescan = holder.RescanCommand.CanExecute(null);
                    ignored = holder.ScanFolderAsync(other.Path);
                }
            });
        holder = vm;
        await vm.LoadAsync();

        await vm.ScanFolderAsync(_games.Path);

        Assert.True(observed);
        Assert.False(canChoose);
        Assert.False(canRescan);
        Assert.Same(vm.Scanning, ignored);
        Assert.Equal(other.Path, vm.Scan.Folder.TrimEnd('\\'), ignoreCase: true);
        Assert.Equal(2, File.Load().Roots.Count);
        Assert.False(vm.IsScanBusy);
        Assert.True(vm.ChooseFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_leitura_mais_velha_que_termina_por_ultimo_nao_sobrescreve_a_mais_nova()
    {
        LibrarySamples.Save(_config.Path, LibrarySamples.Library());
        using var release = new ManualResetEventSlim();
        using var firstRead = new ManualResetEventSlim();
        var calls = 0;
        var vm = new LibraryViewModel(
            File,
            new FakeFolderPicker(null),
            action => action(),
            clock: Clock,
            readTranslation: game =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstRead.Set();
                    release.Wait(Ct);
                }

                return LibrarySamples.Translation(game);
            });

        var older = vm.LoadAsync();
        firstRead.Wait(Ct);
        LibrarySamples.Save(_config.Path, LibrarySamples.Library(LibrarySamples.Games().Take(2)));
        var newer = vm.LoadAsync();
        await newer;
        release.Set();
        await older;

        Assert.Equal(2, vm.Cards.Count);
        Assert.Equal("2 jogos na Biblioteca", vm.StatusText);
    }
}
