using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O modo revisão com o resto do app: a paleta, os botões do Painel e o Glossário pela janela principal.</summary>
public sealed class ReviewModeNavigationTests
{
    private static async Task<(MainWindowViewModel Main, string Root)> OpenAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line(1, 0, "美咲", "Bom dia."),
            Line(1, 1, "健太", "Oi."),
        ]);
        folder.SaveText("yst00002",
        [
            new TextUnit("yst00002:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated),
        ]);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female), new CharacterEntry("健太", "Kenta")]);
        folder.SaveScriptNames(ScriptNames.Empty.WithGroup("yst00001", "A").WithGroup("yst00002", "B"));
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        await main.Dashboard.ReloadAsync();
        main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Editor);
        main.Editor.StartReview();
        Assert.True(main.Editor.IsReviewing);
        return (main, root);
    }

    private static TextUnit Line(int script, int n, string speaker, string translation) =>
        new($"yst0000{script}:{n:D6}", TextUnitKind.Dialogue, speaker, $"原文{n}", translation, TranslationStatus.Machine);

    [Fact]
    public async Task Traduzir_pendentes_da_paleta_fecha_o_modo_revisao()
    {
        using var temp = new TempFolder();
        var (main, _) = await OpenAsync(temp);

        main.Palette.Open();
        main.Palette.Query = "Traduzir pendentes";
        main.Palette.SelectedIndex = 0;
        main.Palette.ExecuteSelected();

        Assert.False(main.Editor.IsReviewing);
        Assert.True(main.Editor.HasStatusFilter);
    }

    [Fact]
    public async Task Abrir_outro_capitulo_pelo_Painel_fecha_o_modo_revisao()
    {
        using var temp = new TempFolder();
        var (main, _) = await OpenAsync(temp);

        main.Dashboard.OpenChapterCommand.Execute(main.Dashboard.Chapters.Single(c => c.Name == "B"));

        Assert.False(main.Editor.IsReviewing);
        Assert.Equal("yst00002", main.Editor.SelectedScript?.Key);
    }

    [Fact]
    public async Task Linha_aberta_por_busca_ou_Conferencia_fecha_o_modo_revisao()
    {
        using var temp = new TempFolder();
        var (main, _) = await OpenAsync(temp);

        Assert.True(await main.Editor.GoToLineAsync("yst00002", "yst00002:000000"));

        Assert.False(main.Editor.IsReviewing);
        Assert.Equal("yst00002:000000", main.Editor.SelectedLine!.Id);
    }

    [Fact]
    public async Task Definir_o_genero_leva_ao_Glossario_e_ao_voltar_o_aviso_some_com_a_revisao_aberta()
    {
        using var temp = new TempFolder();
        var (main, root) = await OpenAsync(temp);
        var review = main.Editor.Review!;
        review.SkipCommand.Execute(null);
        Assert.True(review.GenderMissing);

        review.DefineGenderCommand.Execute(null);

        Assert.Equal(NavKey.Glossary, main.SelectedNav!.Key);
        Assert.Equal(GlossaryTab.Characters, main.Glossary.SelectedTab);

        ProjectFolder.Open(root).SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female), new CharacterEntry("健太", "Kenta", CharacterGender.Male)]);
        main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Editor);

        Assert.True(main.Editor.IsReviewing);
        Assert.Same(review, main.Editor.Review);
        Assert.False(review.GenderMissing);
        Assert.Equal(1, review.Position);
    }
}
