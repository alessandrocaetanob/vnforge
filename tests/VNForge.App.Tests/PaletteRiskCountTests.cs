using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// A contagem de linhas de risco alto do subtítulo "Revisar por risco" na paleta: sem dados no Editor (texto carregando, ou a leitura falhou) ela volta na hora com
/// nulo, e refazer a paleta por causa dela não pode chamar a contagem de novo, num ciclo sem fim.
/// </summary>
public sealed class PaletteRiskCountTests
{

    private static string CreateProject(TempFolder temp, bool corrupt = false)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, "美咲", "原文", "Bom dia.", TranslationStatus.Machine)]);
        if (corrupt)
        {
            File.WriteAllText(Path.Combine(root, "text", "yst00001.json"), "isto não é json");
        }

        return root;
    }

    private static MainWindowViewModel NewMain(TempFolder temp, string root) =>
        new(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));

    [Fact]
    public async Task Com_a_leitura_do_texto_falha_e_o_Editor_sem_dados_a_paleta_abre_sem_estourar_a_pilha()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp, corrupt: false);
        var main = NewMain(temp, root);
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.ProjectOpening;
        main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Editor);
        await main.Editor.Loading;

        // O projeto abriu, e o texto estraga antes de uma releitura: ela falha e o Editor fica sem dados. O Painel e a Conferência
        // ainda podem estar lendo o mesmo arquivo em segundo plano, por isso a gravação espera a leitura terminar.
        TestFiles.WriteAllTextWhileReadersFinish(Path.Combine(root, "text", "yst00001.json"), "isto não é json");
        main.Editor.Reload();
        await main.Editor.Loading;
        Assert.NotNull(main.Editor.ErrorMessage);

        main.Palette.Open();
        main.Palette.Query = "risco";
        await main.RiskCounting!;

        var entry = Assert.Single(main.Palette.Results, r => r.Title == "Revisar por risco");
        Assert.Equal("O que mais precisa de você, no projeto todo", entry.Subtitle);
    }
}
