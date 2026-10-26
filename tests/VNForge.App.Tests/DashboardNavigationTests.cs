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
    public async Task O_proximo_passo_abre_o_Editor_na_cena_com_japones()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        Assert.Equal("Traduzir a parte 1", main.Dashboard.NextStepLabel);

        main.Dashboard.OpenNextStepCommand.Execute(null);

        Assert.Equal(NavKey.Editor, main.SelectedNav!.Key);
        Assert.Equal("yst00002", main.Editor.SelectedScript?.Key);
    }

    [Theory]
    [InlineData("Settings", NavKey.Settings)]
    [InlineData("Glossary", NavKey.Glossary)]
    [InlineData("Conference", NavKey.Conference)]
    [InlineData("Editor", NavKey.Editor)]
    [InlineData("Test", NavKey.Build)]
    public async Task Os_links_do_Painel_trocam_para_a_tela_pedida(string link, NavKey expected)
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);

        switch (link)
        {
            case "Settings": main.Dashboard.OpenSettingsCommand.Execute(null); break;
            case "Glossary": main.Dashboard.OpenGlossaryCommand.Execute(null); break;
            case "Conference": main.Dashboard.OpenConferenceCommand.Execute(null); break;
            case "Test": main.Dashboard.OpenTestCommand.Execute(null); break;
            default: main.Dashboard.OpenEditorCommand.Execute(null); break;
        }

        await main.ConferenceLoading;
        await main.Build.Loading;
        Assert.Equal(expected, main.SelectedNav!.Key);
    }

    [Theory]
    [InlineData(JourneyStep.Translate, NavKey.Editor)]
    [InlineData(JourneyStep.Review, NavKey.Editor)]
    [InlineData(JourneyStep.Check, NavKey.Conference)]
    [InlineData(JourneyStep.Test, NavKey.Build)]
    public async Task Um_passo_da_jornada_leva_a_tela_dele(JourneyStep step, NavKey expected)
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);

        main.Dashboard.OpenStepCommand.Execute(step);
        await main.ConferenceLoading;
        await main.Build.Loading;

        Assert.Equal(expected, main.SelectedNav!.Key);
    }

    [Fact]
    public async Task Um_item_de_Precisa_de_voce_leva_ao_Glossario_e_o_capitulo_ao_Editor_na_cena_dele()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        ProjectFolder.Open(main.CurrentProject!.Root).SaveCharacters([new VNForge.Core.Glossary.CharacterEntry("美咲", "Misaki")]);
        await main.Dashboard.ReloadAsync();

        main.Dashboard.Todos.Single(t => t.Title == "1 personagem sem gênero").Command!.Execute(null);
        Assert.Equal(NavKey.Glossary, main.SelectedNav!.Key);

        main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Dashboard);
        main.Dashboard.OpenChapterCommand.Execute(main.Dashboard.Chapters.Single());
        Assert.Equal(NavKey.Editor, main.SelectedNav!.Key);
        Assert.Equal("yst00002", main.Editor.SelectedScript?.Key);
    }
}
