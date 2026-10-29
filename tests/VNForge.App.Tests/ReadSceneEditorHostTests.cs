using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O leitor de cena (#475) dentro da tela do Editor: o que some e perde o foco enquanto se lê, o foco na volta e a barra com o chip "Soa estranho" aparecendo.</summary>
public sealed class ReadSceneEditorHostTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static async Task<EditorViewModel> OpenAsync(TempFolder temp, bool oneMarked = false)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated);
        if (oneMarked)
        {
            var folder = ProjectFolder.Open(root);
            var units = folder.LoadText("yst00001").ToList();
            units[0] = units[0].WithOdd(true);
            folder.SaveText("yst00001", units);
        }

        var vm = new EditorViewModel(postToUi: acao => acao(), layoutMemory: new Services.EditorLayoutMemory());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        return vm;
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task Enquanto_se_le_o_cabecalho_a_barra_e_a_grade_saem_da_tela_e_voltam_ao_sair_com_o_foco_no_campo_da_traducao()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1440, Height = 900, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                var header = window.GetVisualDescendants().OfType<PageHeader>().Single();
                var columns = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "Columns");
                Assert.True(header.IsEffectivelyVisible && columns.IsEffectivelyVisible);

                vm.StartReading();
                Settle(window);
                Assert.False(header.IsEffectivelyVisible, "o cabeçalho sai da tela e do foco");
                Assert.False(columns.IsEffectivelyVisible, "a grade e o inspetor saem da tela e do foco");
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextEditor>(), t => t.IsEffectivelyVisible);

                vm.StopReading();
                Settle(window);
                Assert.True(header.IsEffectivelyVisible && columns.IsEffectivelyVisible);
                var field = window.GetVisualDescendants().OfType<LineDetailView>().Single().GetVisualDescendants().OfType<TextEditor>().Single();
                Assert.True(field.TextArea.IsFocused, "o foco volta ao campo da tradução");

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Alt_seta_nao_muda_a_linha_enquanto_se_le()
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);
        vm.SelectedLine = vm.Lines[0];
        vm.StartReading();

        vm.SelectNextCommand.Execute(null);
        vm.SelectPreviousCommand.Execute(null);

        Assert.Same(vm.Lines[0], vm.SelectedLine);
        vm.StopReading();
        vm.SelectNextCommand.Execute(null);
        Assert.Same(vm.Lines[1], vm.SelectedLine);
    }

    [Theory]
    [InlineData(960)]
    [InlineData(1366)]
    public async Task Ao_sair_do_leitor_o_chip_Soa_estranho_ligado_fica_a_vista_na_faixa(int windowWidth)
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = windowWidth, Height = 800, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                vm.StartReading();
                Settle(window);
                vm.Reading!.ToggleOddCommand.Execute(null);
                vm.StopReading();
                Settle(window);
                Settle(window);

                var chip = window.GetVisualDescendants().OfType<FilterChip>().Single(c => c.IsEffectivelyVisible && c.IsChecked == true);
                var strip = chip.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.Equal("Soa estranho", chip.Label);
                var left = chip.TranslatePoint(default, strip)!.Value.X;
                Assert.True(left >= -0.5 && left + chip.Bounds.Width <= strip.Bounds.Width + 0.5, $"o chip ligado está dentro da faixa ({left:0} a {left + chip.Bounds.Width:0} de {strip.Bounds.Width:0})");
                window.Close();
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(960, true)]
    [InlineData(1366, true)]
    [InlineData(1440, false)]
    public async Task Com_o_chip_Soa_estranho_visivel_os_filtros_cabem_na_faixa_ou_rolam_sem_cobrir_as_acoes(int windowWidth, bool iconsOnly)
    {
        using var temp = new TempFolder();
        var vm = await OpenAsync(temp, oneMarked: true);
        var failures = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = windowWidth, Height = 800, Content = new EditorView { DataContext = vm } };
                window.Show();
                Settle(window);
                var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(c => c.IsEffectivelyVisible).ToList();
                var tools = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tool") && !b.Classes.Contains("scenes-trigger") && b.IsEffectivelyVisible).ToList();
                var strip = chips[0].GetVisualAncestors().OfType<ScrollViewer>().First();
                Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);

                if (chips.Count != 6)
                {
                    failures.Add($"esperava 6 filtros visíveis, achei {chips.Count}");
                }

                if (chips.Any(c => c.Bounds.Width <= 0))
                {
                    failures.Add("um filtro ficou sem largura");
                }

                // A faixa rola; o que ela mostra nunca passa por cima das ações, e o que não cabe fica atrás da borda dela, não solto sobre os botões.
                var stripBox = Box(strip);
                if (stripBox.Right > tools.Min(t => Box(t).X))
                {
                    failures.Add($"a faixa de filtros ({stripBox.Right:0}) passa por cima das ações ({tools.Min(t => Box(t).X):0})");
                }

                var clipped = chips.Where(c => Box(c).Right > stripBox.Right + 0.5).ToList();
                // A 960 px a barra já enchia com cinco filtros: o sexto só cabe rolando a faixa. Daí para cima nada pode ficar cortado.
                if (clipped.Count > 0 && (windowWidth > 960 || strip.Extent.Width <= strip.Viewport.Width))
                {
                    failures.Add($"{clipped.Count} filtro(s) cortado(s) pela faixa: {string.Join(", ", clipped.Select(c => c.Label))} (direita {Box(clipped[^1]).Right:0} contra {stripBox.Right:0})");
                }

                if (clipped.Count == 0 && chips.Select(c => Math.Round(Box(c).Y)).Distinct().Count() != 1)
                {
                    failures.Add("os filtros não estão na mesma linha");
                }

                var labels = tools.Select(t => t.GetVisualDescendants().OfType<TextBlock>().Single().IsEffectivelyVisible).ToList();
                if (labels.Any(shown => shown == iconsOnly))
                {
                    failures.Add($"rótulos dos botões secundários: {string.Join(",", labels)} (só ícones esperado: {iconsOnly})");
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
