using Avalonia;
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

/// <summary>A tela do Editor montada de verdade: medidas do artboard, inspetor sem quebra e a busca por atalho.</summary>
public sealed class EditorV2HeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
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
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated, TranslationStatus.Translated);
        var vm = new EditorViewModel(postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[1];
        return (vm, temp);
    }

    private static Window Show(Control content, int width, int height = 1000)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Settle(window);
        return window;
    }

    [Fact]
    public async Task Ctrl_F_e_Ctrl_H_abrem_o_painel_de_busca_dentro_do_Editor()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var openAfterF = false;
        var replaceAfterF = true;
        var openAfterH = false;
        var replaceAfterH = false;
        var focused = string.Empty;
        var focusAfterEsc = string.Empty;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1440);
                var editor = (EditorView)window.Content!;
                var before = (window.FocusManager?.GetFocusedElement() as Control)?.GetType().Name;
                Assert.True(editor.FindControl<ListBox>("LinesList")!.ContainerFromIndex(0)!.Focus());

                window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
                window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
                Settle(window);
                openAfterF = vm.IsSearchOpen;
                replaceAfterF = vm.IsReplaceVisible;
                focused = (window.FocusManager?.GetFocusedElement() as Control)?.Name ?? string.Empty;
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Settle(window);
                var closed = !vm.IsSearchOpen;
                focusAfterEsc = (window.FocusManager?.GetFocusedElement() as Control)?.GetType().Name ?? "nada";
                Assert.True(closed, $"Esc não fechou: foco em {focused}, aberto={vm.IsSearchOpen}, antes={before}");

                window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, null);
                window.KeyRelease(Key.H, RawInputModifiers.Control, PhysicalKey.H, null);
                Settle(window);
                openAfterH = vm.IsSearchOpen;
                replaceAfterH = vm.IsReplaceVisible;
                window.Close();
            },
            CancellationToken.None);

        Assert.True(openAfterF);
        Assert.False(replaceAfterF);
        Assert.Equal("PatternBox", focused);
        Assert.True(openAfterH, $"foco depois do Esc: {focusAfterEsc}");
        Assert.True(replaceAfterH);
    }

    [Fact]
    public async Task Clicar_num_resultado_da_lista_leva_a_grade_a_linha()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        vm.OpenSearchCommand.Execute(null);
        vm.Search.Pattern = "tradução 3";
        await vm.Search.FindCommand.ExecuteAsync(null);
        string? selected = null;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1440);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ResultsList");
                Assert.Equal(1, list.ItemCount);
                list.SelectedIndex = 0;
                Settle(window);
                selected = vm.SelectedLine?.Id;
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal("yst00001:000003", selected);
    }
}
