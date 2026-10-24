using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O fluxo do Início ao resto do app: "Abrir projeto" leva ao Painel e "Continuar em" ao Editor, no script em que o usuário parou.</summary>
public class MainWindowStartFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MainWindowViewModel Create(TempFolder temp) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now)));

    private static string TwoScripts(TempFolder temp) =>
        StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines:
        [
            ("yst00001", TranslationStatus.Approved),
            ("yst00002", TranslationStatus.Machine),
            ("yst00003", TranslationStatus.Untranslated),
        ]);

    [Fact]
    public async Task Abrir_projeto_do_detalhe_leva_ao_Painel_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        await vm.Start.OpenAsync(TwoScripts(temp));
        Assert.Same(vm.Start, vm.CurrentPage);

        await vm.Start.OpenSelectedCommand.ExecuteAsync(null);
        await vm.Dashboard.ReloadAsync();
        await vm.Editor.Loading;

        Assert.Equal(NavKey.Dashboard, vm.SelectedNav?.Key);
        Assert.Same(vm.Dashboard, vm.CurrentPage);
        Assert.Equal("Minha VN", vm.Dashboard.Project?.Name);
    }

    [Fact]
    public async Task Continuar_em_abre_o_Editor_no_ultimo_script_editado()
    {
        using var temp = new TempFolder();
        var root = TwoScripts(temp);
        File.SetLastWriteTimeUtc(Path.Combine(root, "text", "yst00001.json"), Now.UtcDateTime.AddDays(-3));
        File.SetLastWriteTimeUtc(Path.Combine(root, "text", "yst00002.json"), Now.UtcDateTime);
        var vm = Create(temp);
        await vm.Start.OpenAsync(root);

        vm.Start.OpenSelectedInEditorCommand.Execute(null);
        await vm.LineNavigation;

        Assert.Same(vm.Editor, vm.CurrentPage);
        Assert.Equal("yst00002", vm.Editor.SelectedScript?.Key);
    }
}
