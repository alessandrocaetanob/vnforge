using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class MainWindowBuildNavTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now)));

    [Fact]
    public void Janela_fecha_quando_nenhuma_operacao_do_Build_esta_rodando()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.True(vm.TryClose());
        Assert.IsType<StartViewModel>(vm.CurrentPage);
    }

    [Fact]
    public async Task Build_fica_desabilitado_ate_abrir_um_projeto_e_depois_navega_para_a_pagina()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, root);
        var item = vm.Nav.Single(n => n.Key == NavKey.Build);
        Assert.False(item.IsEnabled);

        vm.SelectedNav = item;
        Assert.IsType<StartViewModel>(vm.CurrentPage);

        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        vm.SelectedNav = item;
        await vm.Build.Loading;

        Assert.True(item.IsEnabled);
        Assert.Same(vm.Build, vm.CurrentPage);
        Assert.Equal("Minha VN", vm.Build.Project?.Name);
    }
}
