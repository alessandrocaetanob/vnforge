using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O shell da #544: Biblioteca no trilho, o jogo aberto com o seu cabeçalho e as abas Ficha, Tradução e Jogar; a tradução de antes vive numa segunda linha.</summary>
public sealed class MainWindowShellTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string root) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));

    private static async Task<MainWindowViewModel> OpenedAsync(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses.Length == 0 ? [TranslationStatus.Machine, TranslationStatus.Approved] : statuses);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    private static NavItemViewModel Item(MainWindowViewModel vm, NavKey key) => vm.Nav.Single(n => n.Key == key);

    [Fact]
    public async Task A_barra_de_status_diz_quantos_jogos_a_Biblioteca_tem_e_o_que_o_app_sabe_do_VNDB()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        Assert.Equal("Nenhum jogo ainda", vm.LibraryCountText);
        Assert.Equal("VNDB: sem consulta ainda", vm.VndbStatusText);
        Assert.Equal(MainWindowViewModel.VndbConnection.Unknown, vm.Vndb);
        Assert.False(vm.ShowWorkStatus);

        vm.SelectedNav = Item(vm, NavKey.Editor);
        Assert.True(vm.ShowWorkStatus);
        vm.SelectedNav = Item(vm, NavKey.Dashboard);
        Assert.False(vm.ShowWorkStatus);
    }

    [Fact]
    public async Task O_titulo_em_japones_do_cabecalho_vem_do_vinculo_com_o_VNDB_do_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", [TranslationStatus.Machine]);
        ProjectFolder.Open(root).SaveVndbLink(new VNForge.Core.Vndb.VndbLink("v31201", "Amaoto no Hakoniwa", "雨音の箱庭", null, []));
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("雨音の箱庭", vm.GameOriginalTitle);
        Assert.True(vm.HasGameOriginalTitle);
    }

    [Fact]
    public async Task Sem_o_vinculo_com_o_VNDB_o_cabecalho_nao_tem_titulo_em_japones()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        Assert.Equal(string.Empty, vm.GameOriginalTitle);
        Assert.False(vm.HasGameOriginalTitle);
    }

    [Fact]
    public void Sem_projeto_nao_ha_jogo_aberto_e_a_migalha_e_so_a_Biblioteca()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        Assert.False(vm.InGame);
        Assert.False(vm.ShowSections);
        Assert.Equal("Biblioteca", vm.Breadcrumb);
        Assert.Null(vm.SelectedGameTab);
    }

    [Fact]
    public async Task Abrir_o_projeto_na_Biblioteca_ainda_nao_entra_no_jogo()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        Assert.True(vm.HasProject);
        Assert.False(vm.InGame);
        Assert.Same(vm.Library, vm.CurrentPage);
        Assert.Equal("Biblioteca", vm.Breadcrumb);
    }

    [Fact]
    public async Task Entrar_no_Painel_abre_o_jogo_na_aba_Traducao_e_a_migalha_leva_o_nome()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        vm.SelectedNav = Item(vm, NavKey.Dashboard);

        Assert.True(vm.InGame);
        Assert.True(vm.ShowSections);
        Assert.Equal(GameTab.Translation, vm.SelectedGameTab?.Tab);
        Assert.Equal(NavKey.Dashboard, vm.SelectedSection?.Key);
        Assert.Equal("Biblioteca › Minha VN", vm.Breadcrumb);
    }

    [Fact]
    public void As_abas_do_jogo_sao_Ficha_Traducao_e_Jogar_e_as_secoes_da_traducao_seguem_a_ordem_do_quadro()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        Assert.Equal(["Ficha", "Tradução", "Jogar"], vm.GameTabs.Select(t => t.Label));
        Assert.Equal(["Painel", "Editor", "Revisão", "Conferir", "Testar", "Glossário", "Mais"], vm.Sections.Select(s => s.Label));
    }

    [Fact]
    public void O_trilho_tem_a_Biblioteca_no_alto_e_Ajustes_no_pe()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        Assert.Equal(["Biblioteca"], vm.TopNav.Select(n => n.Label));
        Assert.Equal(["Ajustes"], vm.BottomNav.Select(n => n.Label));
        Assert.Same(vm.TopNav[0], vm.SelectedTopNav);
    }

    [Fact]
    public async Task Dentro_do_jogo_a_Biblioteca_continua_marcada_no_trilho_e_clicar_nela_sai_do_jogo()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedNav = Item(vm, NavKey.Editor);

        Assert.Same(vm.TopNav[0], vm.SelectedTopNav);
        Assert.Null(vm.SelectedBottomNav);

        vm.OpenLibraryCommand.Execute(null);

        Assert.False(vm.InGame);
        Assert.Same(vm.Library, vm.CurrentPage);
    }

    [Fact]
    public async Task Ajustes_nao_e_do_jogo_e_o_trilho_deixa_de_marcar_a_Biblioteca()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedNav = Item(vm, NavKey.Dashboard);

        vm.SelectedBottomNav = vm.BottomNav[0];

        Assert.False(vm.InGame);
        Assert.Same(vm.Settings, vm.CurrentPage);
        Assert.Null(vm.SelectedTopNav);
        Assert.Equal("Biblioteca › Minha VN", vm.Breadcrumb);
    }

    [Fact]
    public void Ajustes_sem_projeto_aberto_so_dizem_Ajustes_na_barra_de_titulo()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        vm.SelectedBottomNav = vm.BottomNav[0];

        Assert.Equal("Ajustes", vm.Breadcrumb);
        Assert.False(vm.CanReturnToGame);
    }

    [Fact]
    public async Task Dos_Ajustes_ou_da_Biblioteca_um_botao_volta_ao_ultimo_jogo_aberto()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        Assert.False(vm.CanReturnToGame);
        vm.SelectedNav = Item(vm, NavKey.Editor);
        Assert.False(vm.CanReturnToGame);

        vm.SelectedBottomNav = vm.BottomNav[0];
        Assert.True(vm.CanReturnToGame);
        vm.ReturnToGameCommand.Execute(null);

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.True(vm.InGame);
        Assert.False(vm.CanReturnToGame);

        vm.OpenLibraryCommand.Execute(null);
        Assert.True(vm.CanReturnToGame);
        vm.ReturnToGameCommand.Execute(null);
        Assert.Same(vm.Editor, vm.CurrentPage);
    }

    [Fact]
    public async Task Cada_secao_da_traducao_abre_a_sua_pagina_dentro_do_jogo()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        foreach (var (key, page) in new (NavKey, Func<object>)[]
        {
            (NavKey.Dashboard, () => vm.Dashboard),
            (NavKey.Editor, () => vm.Editor),
            (NavKey.Conference, () => vm.Conference),
            (NavKey.Build, () => vm.Build),
            (NavKey.Glossary, () => vm.Glossary),
        })
        {
            vm.SelectedSection = vm.Sections.Single(s => s.Key == key);
            await vm.ConferenceLoading;

            Assert.Same(page(), vm.CurrentPage);
            Assert.True(vm.InGame);
            Assert.Equal(GameTab.Translation, vm.SelectedGameTab?.Tab);
        }
    }

    [Fact]
    public async Task O_Mais_abre_Lancar_Biblia_e_Imagens_e_fica_marcado_enquanto_uma_delas_e_a_pagina()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        Assert.Equal(["Lançar", "Bíblia", "Imagens", "Provas"], vm.MoreSections.Select(s => s.Label));
        Assert.Same(vm.Sections[^1], vm.MoreSection);
        Assert.Equal("Mais", vm.MoreSection.Label);
        Assert.True(vm.MoreSection.HasMenu);

        foreach (var (key, page) in new (NavKey, Func<object>)[] { (NavKey.Release, () => vm.Release), (NavKey.Bible, () => vm.Bible), (NavKey.Images, () => vm.Images) })
        {
            vm.SelectedSection = vm.Sections.Single(s => s.Key == NavKey.Dashboard);
            Assert.NotSame(vm.MoreSection, vm.SelectedSection);

            vm.OpenSectionCommand.Execute(vm.MoreSections.Single(s => s.Key == key));
            await vm.ConferenceLoading;

            Assert.Same(page(), vm.CurrentPage);
            Assert.True(vm.InGame);
            Assert.Equal(GameTab.Translation, vm.SelectedGameTab?.Tab);
            Assert.Same(vm.MoreSection, vm.SelectedSection);
        }

        // Escolher o "Mais" na linha não é página: a marca continua na seção que está aberta.
        vm.SelectedSection = vm.MoreSection;
        Assert.Same(vm.Images, vm.CurrentPage);
        Assert.Same(vm.MoreSection, vm.SelectedSection);

        // "Provas" abre o Painel na aba do banco de provas e marca o "Mais"; "Painel" volta à visão geral.
        vm.OpenSectionCommand.Execute(vm.MoreSections.Single(s => s.Key == NavKey.Proofs));
        Assert.Same(vm.Dashboard, vm.CurrentPage);
        Assert.Equal(1, vm.Dashboard.SelectedTab);
        Assert.Same(vm.MoreSection, vm.SelectedSection);
        vm.OpenSectionCommand.Execute(vm.Sections.Single(s => s.Key == NavKey.Dashboard));
        Assert.Equal(0, vm.Dashboard.SelectedTab);
        Assert.Equal(NavKey.Dashboard, vm.SelectedSection?.Key);
    }

    [Fact]
    public async Task Editor_e_Revisao_dividem_a_pagina_e_a_secao_marcada_acompanha_o_modo_revisao()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        var editor = vm.Sections.Single(s => s.Key == NavKey.Editor);
        var review = vm.Sections.Single(s => s.Key == NavKey.Review);
        vm.SelectedSection = editor;
        Assert.Same(editor, vm.SelectedSection);

        vm.SelectedSection = review;

        Assert.True(vm.Editor.IsReviewing);
        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Same(review, vm.SelectedSection);
        Assert.True(vm.InGame);
        Assert.False(vm.ShowGameHeader);

        vm.SelectedSection = editor;

        Assert.False(vm.Editor.IsReviewing);
        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Same(editor, vm.SelectedSection);
        Assert.True(vm.ShowGameHeader);
    }

    [Fact]
    public async Task Fechar_o_modo_revisao_pelo_proprio_modo_devolve_a_marca_ao_Editor()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedSection = vm.Sections.Single(s => s.Key == NavKey.Review);
        Assert.Equal(NavKey.Review, vm.SelectedSection?.Key);

        vm.Editor.StopReview();

        Assert.Equal(NavKey.Editor, vm.SelectedSection?.Key);
    }

    [Fact]
    public async Task Escolher_nenhuma_secao_nao_muda_a_pagina()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedSection = vm.Sections.Single(s => s.Key == NavKey.Dashboard);

        vm.SelectedSection = null;

        Assert.Equal(NavKey.Dashboard, vm.SelectedSection?.Key);
    }

    [Fact]
    public async Task As_abas_Ficha_e_Jogar_abrem_as_paginas_do_jogo_e_Traducao_volta_a_ultima_secao()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedSection = vm.Sections.Single(s => s.Key == NavKey.Editor);

        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Sheet);

        Assert.Same(vm.Sheet, vm.CurrentPage);
        Assert.True(vm.InGame);
        Assert.False(vm.ShowSections);
        Assert.Null(vm.SelectedSection);

        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);

        Assert.Same(vm.Play, vm.CurrentPage);
        Assert.Equal(GameTab.Play, vm.SelectedGameTab?.Tab);

        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Translation);

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedSection?.Key);
    }

    [Fact]
    public async Task Ficha_e_Jogar_oferecem_o_caminho_para_a_Traducao()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);

        Assert.True(vm.Play.ActionCommand.CanExecute(null));
        vm.Play.ActionCommand.Execute(null);

        Assert.Same(vm.Build, vm.CurrentPage);
        Assert.Equal(GameTab.Translation, vm.SelectedGameTab?.Tab);

        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Sheet);
        vm.Sheet.ActionCommand.Execute(null);

        Assert.Same(vm.Dashboard, vm.CurrentPage);
    }

    [Fact]
    public void As_abas_so_valem_com_projeto_aberto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        vm.SelectedGameTab = vm.GameTabs[1];

        Assert.Same(vm.Library, vm.CurrentPage);
        Assert.False(vm.InGame);
        Assert.All(vm.Sections, s => Assert.False(s.IsEnabled));
    }

    [Fact]
    public async Task O_contador_de_erros_fica_na_secao_Conferir()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[0] = units[0] with { Source = @"「行こう」\p", Translation = "Vamos" };
        folder.SaveText("yst00001", units);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        var conference = vm.Sections.Single(s => s.Key == NavKey.Conference);

        Assert.Same(Item(vm, NavKey.Conference), conference);
        Assert.Equal(1, conference.Badge);
        Assert.Equal("1", conference.BadgeText);
    }

    [Fact]
    public async Task O_cabecalho_do_jogo_mostra_a_engine_e_o_andamento_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp, TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        vm.SelectedNav = Item(vm, NavKey.Dashboard);
        await vm.Dashboard.ReloadAsync();

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.Equal("Yu-RIS", vm.GameEngineLabel);
        Assert.Equal(50, vm.GameProgressPercent);
        Assert.Equal("Em tradução · 50%", vm.GameProgressLabel);
    }

    [Theory]
    [InlineData(0, 4, "Sem tradução ainda")]
    [InlineData(4, 4, "Traduzido · 100%")]
    [InlineData(1, 4, "Em tradução · 25%")]
    public void O_andamento_do_jogo_tem_texto_para_nao_iniciado_e_para_pronto(int ready, int total, string expected)
    {
        Assert.Equal(expected, GameProgress.Describe(ready, total));
    }

    [Fact]
    public async Task O_Painel_tem_o_proximo_passo_que_o_cabecalho_mostra_no_botao()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp, TranslationStatus.Machine, TranslationStatus.Machine);
        vm.SelectedNav = Item(vm, NavKey.Dashboard);
        await vm.Dashboard.ReloadAsync();

        Assert.True(vm.Dashboard.HasNextStep);
        Assert.StartsWith("Revisar", vm.Dashboard.NextStepLabel, StringComparison.Ordinal);
        Assert.True(vm.Dashboard.OpenNextStepCommand.CanExecute(null));
    }

    [Fact]
    public async Task Entrar_direto_pelo_Editor_tambem_carrega_o_proximo_passo_do_cabecalho()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp, TranslationStatus.Machine, TranslationStatus.Machine);
        Assert.False(vm.Dashboard.HasNextStep);

        vm.SelectedNav = Item(vm, NavKey.Editor);
        await vm.DashboardRefresh;

        Assert.True(vm.Dashboard.HasNextStep);
        Assert.Equal("Sem tradução ainda", vm.GameProgressLabel);
        Assert.True(vm.GameProgressNone);
    }

    [Fact]
    public async Task Aprovar_uma_linha_no_Editor_atualiza_a_pilula_e_a_barra_do_cabecalho()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)))
        {
            Editor = new EditorViewModel(clock: new FixedClock(Now), postToUi: a => a()),
            ProgressRefreshDelay = TimeSpan.Zero,
        };
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        vm.SelectedNav = Item(vm, NavKey.Editor);
        await vm.DashboardRefresh;
        Assert.Equal("Em tradução · 50%", vm.GameProgressLabel);
        Assert.False(vm.GameProgressDone);

        vm.Editor.SelectedLine = vm.Editor.Lines[0];
        vm.Editor.Detail!.ApproveCommand.Execute(null);
        await vm.DashboardRefresh;

        Assert.Equal(100, vm.GameProgressPercent);
        Assert.Equal("Traduzido · 100%", vm.GameProgressLabel);
        Assert.True(vm.GameProgressDone);
    }

    [Fact]
    public async Task Falha_ao_reler_o_Painel_depois_de_gravar_vira_aviso_e_nao_tarefa_perdida()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)))
        {
            Editor = new EditorViewModel(clock: new FixedClock(Now), postToUi: a => a()),
            ProgressRefreshDelay = TimeSpan.Zero,
        };
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        vm.SelectedNav = Item(vm, NavKey.Editor);
        await vm.DashboardRefresh;

        vm.Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.IsLoading))
            {
                throw new InvalidOperationException("falha ao reler");
            }
        };
        vm.Editor.SelectedLine = vm.Editor.Lines[0];
        vm.Editor.Detail!.ApproveCommand.Execute(null);
        await vm.DashboardRefresh;

        Assert.True(vm.DashboardRefresh.IsCompletedSuccessfully);
        var toast = Assert.Single(vm.Toasts.Toasts, t => t.IsWarning);
        Assert.Contains("andamento", toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_paleta_lista_a_Biblioteca_e_as_paginas_do_jogo_como_telas()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);

        vm.Palette.Open();
        foreach (var screen in new[] { "Biblioteca", "Ficha", "Jogar", "Painel" })
        {
            vm.Palette.Query = screen;
            Assert.Contains(vm.Palette.Results, r => r.Title == screen && r.Subtitle == "Tela");
        }
    }

    [Fact]
    public void Nome_curto_da_engine_para_a_etiqueta()
    {
        Assert.Equal("Yu-RIS", ProjectLabels.EngineName("yuris"));
        Assert.Equal("KiriKiri", ProjectLabels.EngineName("kirikiri"));
        Assert.Equal("siglus", ProjectLabels.EngineName("siglus"));
    }
}
