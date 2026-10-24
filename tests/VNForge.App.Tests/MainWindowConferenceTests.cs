using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowConferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<MainWindowViewModel> OpenedWithProblem(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Conferencia_fica_desabilitada_ate_abrir_um_projeto_e_depois_lista_os_problemas()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);
        Assert.False(item.IsEnabled);
        Assert.Equal("Conferência", item.Label);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        Assert.True(item.IsEnabled);
        vm.SelectedNav = item;
        await vm.Conference.RefreshAsync();

        Assert.Same(vm.Conference, vm.CurrentPage);
        Assert.Equal("Minha VN", vm.Conference.Project?.Name);
        Assert.Equal("Nenhum problema encontrado.", vm.Conference.EmptyMessage);
    }

    [Fact]
    public async Task Abrir_a_linha_leva_ao_Editor_e_seleciona_a_linha_do_problema()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.Conference.RefreshAsync();
        var row = vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>().Single();

        vm.Conference.OpenLineCommand.Execute(row);
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal("yst00001:000001", vm.Editor.SelectedLine?.Id);
    }

    [Fact]
    public async Task Entrar_na_Conferencia_grava_antes_a_edicao_pendente_do_Editor()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        vm.Editor.SelectedLine = vm.Editor.Lines[2];
        vm.Editor.Detail!.Translation = "editada  agora";

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.Conference.RefreshAsync();

        Assert.Same(vm.Conference, vm.CurrentPage);
        Assert.Contains(vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>(), r => r.UnitId == "yst00001:000002" && r.RuleId == "espaco-duplo");
    }

    [Fact]
    public async Task Gravacao_pendente_que_falha_impede_de_entrar_na_Conferencia()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;
        vm.Editor.Detail!.Translation = "não vai gravar";
        File.Delete(ProjectFolder.Open(vm.CurrentProject!.Root).TextPath("yst00001"));

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.NotNull(vm.Editor.Detail?.ErrorMessage);
    }

    [Fact]
    public async Task Marcar_como_ok_na_Conferencia_faz_o_Editor_reler_ao_voltar()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        await vm.Editor.Loading;
        Assert.Single(vm.Editor.Lines, l => l.HasWarning);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.Conference.RefreshAsync();
        vm.Conference.MarkOkCommand.Execute(vm.Conference.Rows.OfType<ConferenceIssueRowViewModel>().Single());
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await vm.Editor.Loading;

        Assert.DoesNotContain(vm.Editor.Lines, l => l.HasWarning);
    }
}
