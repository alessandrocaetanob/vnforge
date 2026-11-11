using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Formatting;
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
        units[1] = WithMarkerError(units[1]);
        folder.SaveText("yst00001", units);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    /// <summary>Uma linha com erro (o <c>\p</c> do original falta na tradução), dos que travam o teste no jogo.</summary>
    private static TextUnit WithMarkerError(TextUnit unit) => unit with { Source = @"「行こう」\p", Translation = "Vamos" };

    private static async Task<MainWindowViewModel> OpenedWith(TempFolder temp, Func<TextUnit, TextUnit>? first, Func<TextUnit, TextUnit>? second)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[0] = first?.Invoke(units[0]) ?? units[0];
        units[1] = second?.Invoke(units[1]) ?? units[1];
        folder.SaveText("yst00001", units);
        var vm = Create(temp, root);
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    private static TextUnit WithWarning(TextUnit unit) => unit with { Translation = "duas  palavras" };

    [Fact]
    public async Task Contador_conta_so_os_erros_e_deixa_os_avisos_de_fora()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWith(temp, WithMarkerError, WithWarning);
        var item = vm.Nav.Single(n => n.Key == NavKey.Conference);

        Assert.Equal(2, vm.Editor.IssueCount);
        Assert.Equal(1, item.Badge);
        Assert.Equal("1", item.BadgeText);
        Assert.False(item.ShowNotice);

        vm.SelectedNav = item;
        await vm.ConferenceLoading;

        Assert.Equal(1, vm.Conference.ErrorCount);
        Assert.Equal(1, item.Badge);
        Assert.False(item.ShowNotice);
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
        Assert.Same(vm.Library, vm.CurrentPage);
        Assert.Null(vm.SelectedBottomNav);
    }
}
