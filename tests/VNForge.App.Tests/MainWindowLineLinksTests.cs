using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O id de linha citado num erro do build ou da Conferência vira link: clicar nele abre o Editor naquela linha.</summary>
public class MainWindowLineLinksTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);

    private static async Task<MainWindowViewModel> Opened(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    [Fact]
    public async Task Clicar_no_erro_do_titulo_do_jogo_abre_os_Ajustes_e_nao_o_Editor()
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;

        vm.Conference.OpenLineByIdCommand.Execute(new LineRequest(GameTitleText.UnitId, GameTitleText.UnitId));
        await vm.LineNavigation;

        Assert.Equal(NavKey.Settings, vm.SelectedNav?.Key);
        Assert.Same(vm.Settings, vm.CurrentPage);
        Assert.Null(vm.Editor.ErrorMessage);
    }
}
