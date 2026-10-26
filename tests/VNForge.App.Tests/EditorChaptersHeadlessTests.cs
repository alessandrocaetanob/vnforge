using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A lista de cenas montada de verdade: um cabeçalho por capítulo e o menu da cena com os capítulos.</summary>
public sealed class EditorChaptersHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<(EditorViewModel Vm, TempFolder Temp)> Abrir()
    {
        var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        var folder = ProjectFolder.Open(root);
        foreach (var key in new[] { "yst00002", "yst00003" })
        {
            folder.SaveText(key, [new TextUnit($"{key}:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        folder.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
        {
            ["yst00001"] = new("a", "userscript/01_cap1"),
            ["yst00002"] = new("b", "userscript/01_cap1"),
            ["yst00003"] = new("c", "userscript/02_cap2"),
        }));
        var vm = new EditorViewModel(postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        return (vm, temp);
    }

    [Fact]
    public async Task A_lista_mostra_um_cabecalho_por_capitulo_e_o_menu_da_cena_traz_os_capitulos()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var titles = new List<string?>();
        var menu = new List<string>();
        var submenu = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1440, Height = 1000, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name is null && l.Classes.Contains("scripts"));
                titles.AddRange(list.GetVisualDescendants().OfType<ScriptGroup>().Where(g => g.IsEffectivelyVisible).Select(g => g.Title));

                var contextMenu = list.ContextMenu!;
                list.Focus();
                list.RaiseEvent(new ContextRequestedEventArgs());
                Settle(window);
                menu.AddRange(contextMenu.Items.OfType<MenuItem>().Select(i => i.Header as string ?? string.Empty));
                submenu.AddRange(contextMenu.Items.OfType<MenuItem>().First().Items.OfType<MenuItem>().Select(i => ((TextBlock)i.Header!).Text!));
                contextMenu.Close();
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal(["Capítulo 1", "Capítulo 2"], titles);
        Assert.Equal(["Capítulo 1", "Capítulo 2"], submenu);
        Assert.Equal(["Mover para o capítulo", "Novo capítulo...", "Voltar ao agrupamento automático"], menu);
    }

    [Fact]
    public async Task Mover_com_a_cena_aberta_depois_de_uma_marcada_mantem_a_aberta_e_as_marcas()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        string? open = null;
        string[] marked = [];

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = new Window { Width = 1440, Height = 1000, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name is null && l.Classes.Contains("scripts"));
                vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00003");
                list.SelectedItems!.Add(vm.Scripts.Single(s => s.Key == "yst00001"));
                Settle(window);

                await vm.MoveToChapterCommand.ExecuteAsync(vm.Chapters.Single(c => c.Name == "Capítulo 2"));
                Settle(window);
                open = vm.SelectedScript?.Key;
                marked = [.. vm.ChosenScripts.Select(s => s.Key).Order()];
                window.Close();
                return true;
            },
            CancellationToken.None);

        Assert.Equal("yst00003", open);
        Assert.Equal(["yst00001", "yst00003"], marked);
    }
}
