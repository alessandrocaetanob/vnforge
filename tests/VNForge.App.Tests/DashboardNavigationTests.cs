using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os botões do Painel levam, pela janela principal, à tela e ao script certos.</summary>
public class DashboardNavigationTests
{
    private static async Task<MainWindowViewModel> OpenAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00002",
        [
            new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated),
        ]);
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        await main.Dashboard.ReloadAsync();
        return main;
    }

    [Fact]
    public async Task Continuar_abre_o_Editor_no_script_com_pendencias()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        Assert.Equal("Continuar em yst00002", main.Dashboard.ContinueLabel);

        main.Dashboard.ContinueCommand.Execute(null);

        Assert.Equal(NavKey.Editor, main.SelectedNav!.Key);
        Assert.Equal("yst00002", main.Editor.SelectedScript?.Key);
    }

    [Theory]
    [InlineData("Settings", NavKey.Settings)]
    [InlineData("Glossary", NavKey.Glossary)]
    [InlineData("Conference", NavKey.Conference)]
    [InlineData("Editor", NavKey.Editor)]
    public async Task Os_links_do_Painel_trocam_para_a_tela_pedida(string link, NavKey expected)
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);

        switch (link)
        {
            case "Settings": main.Dashboard.OpenSettingsCommand.Execute(null); break;
            case "Glossary": main.Dashboard.OpenGlossaryCommand.Execute(null); break;
            case "Conference": main.Dashboard.OpenConferenceCommand.Execute(null); break;
            default: main.Dashboard.OpenEditorCommand.Execute(null); break;
        }

        await main.ConferenceLoading;
        Assert.Equal(expected, main.SelectedNav!.Key);
    }
}
