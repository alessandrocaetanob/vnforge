using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Glossário, Ajustes e o comando "Reler o projeto" mantêm o Editor, o Painel e a Conferência em dia sem reabrir a app (issue #372).</summary>
public class MainWindowRecheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(MainWindowViewModel Vm, ProjectFolder Folder)> Opened(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "そう言いながら亀島は…", "Ele olhou para Kamejima.", TranslationStatus.Approved),
            new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, "おはよう", "duas  palavras", TranslationStatus.Approved),
        ]);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)), toasts: new ToastService(new FakeTimeProvider(), a => a()));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return (vm, folder);
    }

    private static PaletteEntry Rereader(MainWindowViewModel vm)
    {
        vm.Palette.Open();
        vm.Palette.Query = "Reler";
        return Assert.Single(vm.Palette.Results, r => r.Title == "Reler o projeto");
    }

    [Fact]
    public async Task O_comando_Reler_o_projeto_esta_na_paleta_e_atualiza_Editor_e_Conferencia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Opened(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        Assert.Equal(1, vm.Conference.IssueCount);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.Equal(1, vm.Editor.IssueCount);

        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "uma palavra" };
        folder.SaveText("yst00001", units);
        Rereader(vm).Execute();
        await vm.ProjectReloading;
        await vm.Editor.Loading;

        Assert.Equal("uma palavra", vm.Editor.Lines[1].TranslationLabel);
        Assert.Equal(0, vm.Editor.IssueCount);
        Assert.Equal(0, vm.Conference.IssueCount);
    }
}
