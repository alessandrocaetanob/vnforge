using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// Pega texto cortado: um <see cref="TextBlock"/> sem quebra nem reticências que recebe menos largura do que o texto pede
/// aparece com letras a menos ("Apro", "Conferênci"). A largura natural vem de um bloco igual medido sem limite.
/// </summary>
public class ClippedTextHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const double Tolerance = 0.5;

    public static TheoryData<int> Widths => new() { 960, 1440, 2560 };

    internal static List<string> FindClipped(Visual root)
    {
        var found = new List<string>();
        foreach (var block in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Width <= 0
                || block.TextWrapping != Avalonia.Media.TextWrapping.NoWrap || block.TextTrimming != Avalonia.Media.TextTrimming.None)
            {
                continue;
            }

            var natural = new TextBlock
            {
                Text = block.Text,
                FontFamily = block.FontFamily,
                FontSize = block.FontSize,
                FontWeight = block.FontWeight,
                FontStyle = block.FontStyle,
            };
            natural.Measure(Size.Infinity);
            if (block.Bounds.Width + Tolerance < natural.DesiredSize.Width || ClippedByAncestor(block))
            {
                found.Add($"\"{block.Text}\" ({block.Bounds.Width:0.#} de {natural.DesiredSize.Width:0.#})");
            }
        }

        return found;
    }

    /// <summary>O texto cabe no próprio bloco, mas um pai com recorte (botão, item do trilho) é mais estreito do que ele.</summary>
    private static bool ClippedByAncestor(Control block)
    {
        var width = block.Bounds.Width;
        for (var parent = block.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is Button or ListBoxItem && parent is Control c)
            {
                var origin = block.TranslatePoint(default, c);
                return origin is { } p && (p.X < -Tolerance || p.X + width > c.Bounds.Width + Tolerance);
            }
        }

        return false;
    }

    private static Window Open(MainWindowViewModel vm, int width)
    {
        var window = new MainWindow { DataContext = vm, Width = width, Height = 900 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<MainWindowViewModel> OpenedWithProblem(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[1] = units[1] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), clock));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Nenhuma_pagina_do_trilho_tem_texto_cortado(int width)
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        var problems = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Open(vm, width);
                foreach (var item in vm.Nav)
                {
                    vm.SelectedNav = item;
                    Settle(window);
                    problems.AddRange(FindClipped(window).Select(p => $"{item.Label} @{width}: {p}"));
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task Detalhe_da_linha_nao_corta_os_botoes_e_eles_tem_a_mesma_altura(int width)
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        vm.Editor.SelectedLine = vm.Editor.Lines[1];
        var problems = new List<string>();
        var heights = new List<double>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Open(vm, width);
                var detail = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                problems.AddRange(FindClipped(detail));
                var model = (LineDetailViewModel)detail.DataContext!;
                var approve = detail.GetVisualDescendants().OfType<Button>().Where(b => b.Command == model.ApproveAndNextCommand || b.Command == model.AcceptSuggestionCommand).ToList();
                Assert.Equal(2, approve.Count);
                heights.AddRange(approve.Select(b => b.Bounds.Height));
                window.Close();
            },
            CancellationToken.None);

        Assert.Empty(problems);
        Assert.Single(heights.Select(h => Math.Round(h, 1)).Distinct());
    }

    [Fact]
    public async Task Conferencia_em_tela_larga_so_estica_a_lista_de_problemas()
    {
        using var temp = new TempFolder();
        var vm = await OpenedWithProblem(temp);
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Conference);
        await vm.ConferenceLoading;
        var widths = new List<double>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Open(vm, 2560);
                widths.AddRange(window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panel") && b.IsEffectivelyVisible).Select(b => b.Bounds.Width));
                window.Close();
            },
            CancellationToken.None);

        // Regras e detalhe mantêm a largura do artboard; a lista de problemas fica com o resto.
        Assert.Equal(3, widths.Count);
        Assert.Equal(200, widths[0], 1);
        Assert.Equal(340, widths[2], 1);
        Assert.True(widths[1] > 1500);
    }
}
