using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowRailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string root) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));

    private static async Task<MainWindowViewModel> OpenedWithProblem(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    [Fact]
    public void Trilho_tem_os_itens_no_alto_Ajustes_no_pe_e_nenhuma_Busca()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        Assert.Equal(["Início", "Painel", "Editor", "Conferir", "Testar", "Glossário"], vm.TopNav.Select(n => n.Label));
        Assert.Equal(["Ajustes"], vm.BottomNav.Select(n => n.Label));
        Assert.DoesNotContain(vm.Nav, n => n.Label == "Busca");
    }

    [Fact]
    public void Trilho_nao_mostra_Build_e_so_Inicio_e_Ajustes_ficam_habilitados_sem_projeto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        Assert.DoesNotContain(vm.Nav, n => n.Label == "Build");
        Assert.Equal(["Início", "Ajustes"], vm.Nav.Where(n => n.IsEnabled).Select(n => n.Label));
    }

    [Fact]
    public async Task Glossario_so_aparece_com_projeto_aberto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);
        var glossary = vm.Nav.Single(n => n.Key == NavKey.Glossary);
        Assert.False(glossary.IsVisible);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        Assert.True(glossary.IsVisible);
    }

    [Fact]
    public async Task Contador_da_Conferencia_usa_os_problemas_que_o_Editor_leu()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);

        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);

        Assert.Equal(1, item.Badge);
        Assert.True(item.HasBadge);
        Assert.Equal("1", item.BadgeText);
    }

    [Fact]
    public async Task Contador_acompanha_o_Editor_trocado_no_inicializador()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[0] = units[0] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)))
        {
            Editor = new EditorViewModel(),
        };

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        Assert.Equal(1, vm.Nav.Single(n => n.Key == NavKey.Conference).Badge);
    }

    [Fact]
    public async Task Contador_passa_a_valer_o_da_ultima_conferencia()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);

        vm.SelectedNav = item;
        await vm.ConferenceLoading;

        Assert.Equal(vm.Conference.IssueCount, item.Badge);
        Assert.Equal(1, item.Badge);
    }

    [Fact]
    public void Sem_projeto_o_contador_some()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));

        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);

        Assert.Null(item.Badge);
        Assert.False(item.HasBadge);
    }

    [Fact]
    public void Contador_acima_de_99_mostra_99_mais()
    {
        var item = new NavItemViewModel(NavKey.Conference, "Conferir", "Icon.Shield", isEnabled: true) { Badge = 140 };

        Assert.Equal("99+", item.BadgeText);
    }

    [Fact]
    public void Selecionar_Ajustes_pelo_pe_navega_e_limpa_a_selecao_do_alto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, temp.SubFolder("vn"));
        var settings = vm.BottomNav.Single();

        vm.SelectedBottomNav = settings;
        Assert.Same(vm.Settings, vm.CurrentPage);
        Assert.Null(vm.SelectedTopNav);
        Assert.Same(settings, vm.SelectedBottomNav);

        vm.SelectedTopNav = null;
        Assert.Same(vm.Settings, vm.CurrentPage);

        vm.SelectedTopNav = vm.TopNav[0];
        Assert.Same(vm.Start, vm.CurrentPage);
        Assert.Null(vm.SelectedBottomNav);
    }

    [Fact]
    public async Task Barra_de_status_mostra_a_acentuacao_e_os_limites_da_caixa()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        var vm = Create(temp, root);
        Assert.Equal(string.Empty, vm.EncodingText);
        Assert.Equal("Nenhum projeto aberto", vm.ProjectTitle);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.Equal("Acentos: sem acentos", vm.EncodingText);
        Assert.Equal("54 colunas · 3 linhas", vm.BoxLimitsText);
        Assert.Equal(string.Empty, vm.SavedAtText);
    }

    [Fact]
    public async Task Mudar_os_Ajustes_do_projeto_atualiza_a_acentuacao_e_os_limites_da_caixa()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        Assert.Equal("Acentos: sem acentos", vm.EncodingText);
        Assert.Equal("54 colunas · 3 linhas", vm.BoxLimitsText);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Settings.EncodingStrategy = "cp1252";
        vm.Settings.WrapMaxWidth = 40;
        vm.Settings.WrapMaxLines = 2;

        Assert.Equal("Acentos: ajuste no jogo", vm.EncodingText);
        Assert.Equal("40 colunas · 2 linhas", vm.BoxLimitsText);
        Assert.Contains(nameof(MainWindowViewModel.EncodingText), changed);
        Assert.Contains(nameof(MainWindowViewModel.BoxLimitsText), changed);
    }

    [Fact]
    public async Task Hora_da_gravacao_some_ao_abrir_outro_projeto_mas_sobrevive_ao_Reload()
    {
        using var temp = new TempFolder();
        var rootA = SampleProjects.Create(temp.SubFolder("a"), "Projeto A", TranslationStatus.Machine);
        var rootB = SampleProjects.Create(temp.SubFolder("b"), "Projeto B", TranslationStatus.Machine);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(rootA), new FixedClock(Now)))
        {
            Editor = new EditorViewModel(clock: new FixedClock(Now), postToUi: a => a()),
        };
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectLoading;
        vm.Editor.SelectedLine = vm.Editor.Lines[0];
        vm.Editor.Detail!.Translation = "Bom dia.";
        vm.Editor.Detail.Flush();
        Assert.Equal("Salvo às 00:41", vm.SavedAtText);

        vm.Editor.Reload();
        await vm.Editor.Loading;
        Assert.Equal("Salvo às 00:41", vm.SavedAtText);

        vm.Editor.Project = ProjectSummaryReader.Read(rootB);
        await vm.Editor.Loading;
        Assert.Equal(string.Empty, vm.SavedAtText);
    }

    [Fact]
    public async Task Contador_baixa_quando_o_Editor_corrige_uma_linha_depois_da_conferencia()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated, TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)))
        {
            Editor = new EditorViewModel(clock: new FixedClock(Now), postToUi: a => a()),
        };
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);
        vm.SelectedNav = item;
        await vm.ConferenceLoading;
        Assert.Equal(1, item.Badge);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        vm.Editor.SelectedLine = vm.Editor.Lines[0];
        vm.Editor.Detail!.Translation = "Bom dia.";
        vm.Editor.Detail.Flush();

        Assert.Equal(0, vm.Editor.IssueCount);
        Assert.Equal(0, item.Badge);
        Assert.False(item.HasBadge);
    }

    [Fact]
    public async Task Busca_de_linhas_da_paleta_ignora_acentos_na_traducao()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Translated);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        Assert.Single(vm.Editor.FindLines("traducao", 5));
        Assert.Single(vm.Editor.FindLines("TRADUÇÃO", 5));
        Assert.Single(vm.Editor.FindLines("原文0", 5));
    }

    [Fact]
    public void Lista_de_tarefas_conta_as_tarefas_do_servico()
    {
        var service = new BackgroundTaskService(a => a());
        var bar = new TaskBarViewModel(service);
        Assert.Equal("Tarefas (0)", bar.TasksLabel);

        service.Start("Traduzindo yst00014", (_, _) => Task.CompletedTask);
        service.Start("Build", (_, _) => Task.CompletedTask);

        Assert.Equal("Tarefas (2)", bar.TasksLabel);
    }
}
