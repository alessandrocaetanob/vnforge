using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
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
        await vm.Editor.Loading;
        return vm;
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
}
