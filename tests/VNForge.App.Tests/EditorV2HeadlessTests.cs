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
    public async Task A_1440_as_colunas_seguem_o_artboard_scripts_248_e_inspetor_420()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        double scripts = 0, inspector = 0;

        await fixture.Session.Dispatch(
            () =>
            {
                // A janela do app tira 72 px do trilho e 1 px da borda: o Editor recebe 1367 px.
                var window = Show(new EditorView { DataContext = vm }, 1367);
                var panels = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panel") && b.IsEffectivelyVisible).ToList();
                scripts = panels.Single(b => b.Name is null && b.Bounds.Height > 200 && b.Bounds.X < 40).Bounds.Width;
                inspector = panels.Single(b => b.Padding.Left == 16 && b.Bounds.Width > 300).Bounds.Width;
                window.Close();
            },
            CancellationToken.None);

        Assert.InRange(scripts, 247, 249);
        Assert.InRange(inspector, 419, 421);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(420)]
    public async Task Inspetor_nao_quebra_linha_nos_botoes_a_partir_de_400_px(int inspectorWidth)
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var texts = new List<(string Text, double Height)>();
        var rows = new List<double>();

        await fixture.Session.Dispatch(
            () =>
            {
                // O painel tem 16 px de margem de cada lado: o conteúdo recebe a largura menos 32.
                var window = Show(new LineDetailView { DataContext = vm.Detail }, inspectorWidth - 32, 1000);
                var view = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                var detail = (LineDetailViewModel)view.DataContext!;
                var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.Command == detail.ApproveAndNextCommand || b.Command == detail.AcceptSuggestionCommand).ToList();
                Assert.Equal(2, buttons.Count);
                rows.AddRange(buttons.Select(b => b.TranslatePoint(default, view)!.Value.Y));
                foreach (var block in buttons.SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).Where(t => t.IsEffectivelyVisible))
                {
                    texts.Add((block.Text ?? string.Empty, block.Bounds.Height));
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.Equal(rows[0], rows[1], precision: 1);
        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.True(t.Height < 24, $"\"{t.Text}\" ocupa {t.Height:0.#} px de altura: quebrou linha."));
        Assert.Contains(texts, t => t.Text == "Aprovar e próxima");
        Assert.Contains(texts, t => t.Text == "Ctrl+Enter");
    }

    [Fact]
    public async Task Inspetor_mostra_cabecalho_medidas_marcadores_e_contexto()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var shown = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new LineDetailView { DataContext = vm.Detail }, 388);
                foreach (var block in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
                {
                    shown.Add(block.Text!);
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.Contains("yst00001:000001", shown);
        Assert.Contains("ORIGINAL", shown);
        Assert.Contains("TRADUÇÃO", shown);
        Assert.Contains("CONTEXTO", shown);
        Assert.Contains("Marcadores iguais ao original", shown);
        Assert.Contains("Nenhum termo do glossário nesta linha.", shown);
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
                var window = Show(new EditorView { DataContext = vm }, 1367);
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
                var window = Show(new EditorView { DataContext = vm }, 1367);
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

    [Fact]
    public async Task Filtrar_a_lista_de_scripts_esconde_os_outros_sem_tirar_o_script_aberto()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        ProjectFolder.Open(vm.Project!.Root).SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Narration, null, "原文", null, TranslationStatus.Untranslated)]);
        vm.Reload();
        await vm.Loading;
        var visible = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1367);
                vm.ScriptFilter = "yst00002";
                Settle(window);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name is null && l.Classes.Contains("scripts"));
                visible.AddRange(list.GetVisualDescendants().OfType<ScriptItem>().Where(s => s.IsEffectivelyVisible).Select(s => s.Key!));
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal(["yst00002"], visible);
        Assert.Equal("yst00001", vm.SelectedScript?.Key);
    }
}
