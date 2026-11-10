using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A barra do Editor (#394): uma linha só, filtros à esquerda e as Dúvidas à direita. Roteiro, Ler como jogador, Buscar e Traduzir com IA vivem no cabeçalho do jogo (#546).</summary>
public sealed class EditorToolbarTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData(960)]
    [InlineData(1440)]
    public async Task Com_duvida_no_script_o_chip_e_o_botao_Duvidas_aparecem_e_a_barra_segue_numa_linha_sem_sobrepor(int windowWidth)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Machine, TranslationStatus.Untranslated);
        var project = ProjectFolder.Open(root);
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select((u, i) => i == 0 ? u with { Doubt = new TranslationDoubt(DoubtKind.Pun, "Trocadilho.") } : u)]);
        var vm = new EditorViewModel(postToUi: acao => acao(), layoutMemory: new Services.EditorLayoutMemory());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        var failures = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = windowWidth, Height = 800, Content = new EditorView { DataContext = vm } };
                window.Show();
                for (var i = 0; i < 3; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }

                Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
                var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(c => c.IsEffectivelyVisible).ToList();
                var doubtButton = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Command == vm.StartDoubtReviewCommand);
                var tools = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tool") && !b.Classes.Contains("scenes-trigger") && b.IsEffectivelyVisible).ToList();

                if (chips.Count != 6)
                {
                    failures.Add($"esperava 6 chips visíveis, vieram {chips.Count}");
                }

                if (doubtButton is not { IsEffectivelyVisible: true } || !tools.Contains(doubtButton))
                {
                    failures.Add("o botão Dúvidas não está visível na barra");
                }

                if (doubtButton is not null && !(AutomationProperties.GetName(doubtButton) ?? string.Empty).StartsWith("Dúvidas (", StringComparison.Ordinal))
                {
                    failures.Add($"o nome acessível do botão Dúvidas não começa pelo texto visível: '{AutomationProperties.GetName(doubtButton)}'");
                }

                // Quando os seis chips não cabem, a área deles rola e é cortada na borda: o que importa é que essa área não entre nas ações.
                var filters = chips[0].GetVisualAncestors().OfType<ScrollViewer>().First();
                if (Box(filters).Right > tools.Min(t => Box(t).X) + 1)
                {
                    failures.Add("a área dos filtros passa por cima das ações");
                }

                var margin = window.ClientSize.Width < EditorBreakpoints.NarrowMarginBelow ? EditorBreakpoints.NarrowMargin : EditorBreakpoints.WideMargin;
                if (doubtButton is not null && (Box(doubtButton).Right > window.ClientSize.Width - margin + 1 || Box(doubtButton).Right < window.ClientSize.Width - margin - 1))
                {
                    failures.Add($"Dúvidas deve encostar na margem direita ({Box(doubtButton).Right:0} de {window.ClientSize.Width:0})");
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Theory]
    [InlineData(960)]
    [InlineData(1366)]
    [InlineData(1440)]
    [InlineData(1920)]
    public async Task A_barra_fica_numa_linha_com_os_filtros_e_sem_as_acoes_que_foram_para_o_cabecalho(int windowWidth)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated);
        var vm = new EditorViewModel(postToUi: acao => acao(), layoutMemory: new Services.EditorLayoutMemory());
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        var failures = new List<string>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = windowWidth, Height = 800, Content = new EditorView { DataContext = vm } };
                window.Show();
                for (var i = 0; i < 3; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }

                var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(c => c.IsEffectivelyVisible).ToList();
                Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);

                if (chips.Count != 5)
                {
                    failures.Add("os 5 filtros precisam estar visíveis");
                }

                // O chip do quadro tem 32 px: na mesma linha quer dizer o mesmo centro.
                if (chips.Select(c => Math.Round(Box(c).Y + (Box(c).Height / 2))).Distinct().Count() != 1)
                {
                    failures.Add("os filtros não estão na mesma linha");
                }

                var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
                if (buttons.Any(b => b.Command == vm.TranslatePendingCommand || b.Command == vm.StartReadingCommand || b.Command == vm.ToggleSearchCommand))
                {
                    failures.Add("Traduzir com IA, Ler como jogador e Buscar ficam no cabeçalho do jogo, não na barra");
                }

                // O quadro do #546 não tem a linha de título: a barra é a primeira coisa da tela e não há PageHeader.
                if (window.GetVisualDescendants().OfType<PageHeader>().Any())
                {
                    failures.Add("o Editor não tem mais a linha de título");
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
