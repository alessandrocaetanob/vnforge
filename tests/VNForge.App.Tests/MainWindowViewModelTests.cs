using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now)));

    [Fact]
    public void Abre_no_Inicio_com_o_Editor_desabilitado_e_sem_projeto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Equal("Início", vm.SelectedNav?.Label);
        Assert.IsType<StartViewModel>(vm.CurrentPage);
        Assert.False(vm.Nav.Single(n => n.Key == NavKey.Editor).IsEnabled);
        Assert.Null(vm.CurrentProject);
        Assert.Equal("Nenhum projeto aberto", vm.ProjectTitle);
    }

    [Fact]
    public async Task Abrir_projeto_habilita_o_Editor_e_mostra_o_nome_na_barra_de_titulo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("Minha VN", vm.ProjectTitle);
        Assert.True(vm.Nav.Single(n => n.Key == NavKey.Editor).IsEnabled);
        Assert.IsType<StartViewModel>(vm.CurrentPage);
    }

    [Fact]
    public async Task Pedir_o_Editor_navega_e_entrega_o_projeto_ao_Editor()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);

        vm.Start.OpenSelectedInEditorCommand.Execute(null);

        var editor = Assert.IsType<EditorViewModel>(vm.CurrentPage);
        Assert.Equal("Minha VN", editor.Project?.Name);
        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
    }

    [Fact]
    public void Selecionar_item_desabilitado_do_trilho_nao_navega()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);

        Assert.IsType<StartViewModel>(vm.CurrentPage);
        Assert.Equal(NavKey.Start, vm.SelectedNav?.Key);
    }
}
