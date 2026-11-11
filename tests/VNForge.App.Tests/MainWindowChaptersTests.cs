using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os capítulos criados no Editor chegam ao Painel pela janela principal, e "Novo capítulo..." do Painel abre o campo do nome no Editor.</summary>
public class MainWindowChaptersTests
{
    private static async Task<MainWindowViewModel> OpenAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        foreach (var key in new[] { "yst00002", "yst00003" })
        {
            folder.SaveText(key, [new TextUnit($"{key}:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        await main.Dashboard.ReloadAsync();
        return main;
    }

    [Fact]
    public async Task Mover_cenas_no_Editor_atualiza_o_Painel_sem_reler_o_projeto()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        Assert.Equal(["Parte 1"], main.Dashboard.Chapters.Select(c => c.Name));
        main.Editor.SelectedScript = main.Editor.Scripts.Single(s => s.Key == "yst00003");

        main.Editor.BeginNewChapterCommand.Execute(null);
        main.Editor.NewChapterName = "Final";
        await main.Editor.CommitNewChapterCommand.ExecuteAsync(null);

        Assert.Equal(["Parte 1", "Final"], main.Dashboard.Chapters.Select(c => c.Name));
        Assert.Equal("Final", ProjectFolder.Open(main.Editor.Project!.Root).LoadScriptNames().Group("yst00003"));
    }

    [Fact]
    public async Task Novo_capitulo_no_menu_da_linha_do_Painel_abre_o_Editor_na_cena_dela_com_o_campo_do_nome_aberto()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        var row = main.Dashboard.Chapters[0];

        main.Dashboard.NewChapterCommand.Execute(row);
        await main.LineNavigation;

        Assert.Equal(NavKey.Editor, main.SelectedNav!.Key);
        Assert.Equal(row.ScriptKey, main.Editor.SelectedScript?.Key);
        Assert.True(main.Editor.IsNamingChapter);
    }

    [Fact]
    public async Task Renomear_o_capitulo_no_Painel_atualiza_a_lista_do_Editor_e_o_mover_usa_o_nome_novo()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        var row = main.Dashboard.Chapters[0];
        main.Dashboard.BeginRenameChapterCommand.Execute(row);
        row.EditText = "Noite";

        await main.Dashboard.CommitRenameChapterCommand.ExecuteAsync(row);
        await main.Editor.ChapterRefresh;

        Assert.Equal(["Noite"], main.Editor.Chapters.Select(c => c.Name));
        main.Editor.SelectedScript = main.Editor.Scripts.Single(s => s.Key == "yst00003");
        await main.Editor.MoveToChapterCommand.ExecuteAsync(main.Editor.Chapters[0]);
        Assert.Equal(["Noite"], main.Editor.Chapters.Select(c => c.Name));
        Assert.Equal("Noite", ProjectFolder.Open(main.Editor.Project!.Root).LoadScriptNames().Group("yst00003"));
    }

    [Fact]
    public async Task Novo_capitulo_do_Painel_limpa_as_marcas_antigas_e_leva_so_a_cena_aberta()
    {
        using var temp = new TempFolder();
        var main = await OpenAsync(temp);
        main.Editor.ChosenScripts.Add(main.Editor.Scripts.Single(s => s.Key == "yst00001"));
        var row = main.Dashboard.Chapters[0];

        main.Dashboard.NewChapterCommand.Execute(row);
        await main.LineNavigation;

        Assert.True(main.Editor.IsNamingChapter);
        Assert.Equal([row.ScriptKey], main.Editor.ChosenScripts.Select(s => s.Key));
        Assert.Equal("Nome do capítulo novo, com 1 cena.", main.Editor.NewChapterHint);
    }
}
