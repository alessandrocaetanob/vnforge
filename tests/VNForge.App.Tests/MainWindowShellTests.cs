using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Library;
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
    public async Task Jogar_oferece_o_caminho_para_o_Editor()
    {
        using var temp = new TempFolder();
        var vm = await OpenedAsync(temp);
        vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);

        Assert.True(vm.Play.ShowInEditorCommand.CanExecute(null));
        vm.Play.ShowInEditorCommand.Execute(null);

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(GameTab.Translation, vm.SelectedGameTab?.Tab);
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
    public void Nome_curto_da_engine_para_a_etiqueta()
    {
        Assert.Equal("Yu-RIS", ProjectLabels.EngineName("yuris"));
        Assert.Equal("KiriKiri", ProjectLabels.EngineName("kirikiri"));
        Assert.Equal("siglus", ProjectLabels.EngineName("siglus"));
    }

    [Fact]
    public async Task Abrir_um_projeto_que_ja_existe_da_biblioteca_vazia_leva_a_Ficha()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", [TranslationStatus.Machine]);
        var vm = Create(temp, root);

        vm.Library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.Same(vm.Sheet, vm.CurrentPage);
    }

    [Fact]
    public async Task Abrir_o_jogo_da_biblioteca_sem_vndb_json_no_projeto_mostra_a_ficha_do_casamento_da_biblioteca()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", [TranslationStatus.Machine]);
        var game = LibrarySamples.Games().First(g => g.ProjectPath is null && g.Engine.Translatable && g.VndbId is not null) with { ProjectPath = root };
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action());
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        await library.LoadAsync();

        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;

        Assert.Same(vm.Sheet, vm.CurrentPage);
        Assert.Equal(game.VndbId, vm.Sheet.VisualNovelId);
    }
    [Fact]
    public async Task O_mesmo_erro_ao_abrir_o_projeto_do_jogo_aparece_de_novo_na_segunda_vez()
    {
        using var temp = new TempFolder();
        var missing = Path.Combine(temp.Path, "sumiu");
        var game = LibrarySamples.Games().First(g => g.ProjectPath is null && g.Engine.Translatable) with { ProjectPath = missing };
        var file = LibrarySamples.Save(temp.SubFolder("biblioteca"), LibrarySamples.Library([game]));
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action());
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)), library: library);
        await library.LoadAsync();

        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;
        var first = library.ErrorMessage;
        library.ReportProblem(string.Empty);
        library.OpenGameCommand.Execute(library.Cards[0]);
        await vm.ProjectOpening;

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, library.ErrorMessage);
    }

    [Fact]
    public async Task Abrir_um_projeto_que_ja_existe_com_a_pasta_ilegivel_vira_aviso_na_Biblioteca_e_nao_excecao_solta()
    {
        using var temp = new TempFolder();
        var library = new LibraryViewModel(null, new FakeFolderPicker(null), action => action());
        var picker = new ThrowingPicker(new IOException("O disco não respondeu."));
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), picker, new FixedClock(Now)), library: library);

        library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;

        Assert.True(vm.ProjectOpening.IsCompletedSuccessfully);
        Assert.Contains("O disco não respondeu.", library.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_mesmo_erro_ao_abrir_um_projeto_que_ja_existe_aparece_de_novo_na_segunda_vez()
    {
        using var temp = new TempFolder();
        var library = new LibraryViewModel(null, new FakeFolderPicker(null), action => action());
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(Path.Combine(temp.Path, "sumiu")), new FixedClock(Now)), library: library);

        library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;
        var first = library.ErrorMessage;
        library.ReportProblem(string.Empty);
        library.OpenExistingProjectCommand.Execute(null);
        await vm.ProjectOpening;

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, library.ErrorMessage);
    }

    private sealed class ThrowingPicker(Exception error) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromException<string?>(error);
    }
}
