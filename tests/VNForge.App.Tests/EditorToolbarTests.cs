using Avalonia;
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

/// <summary>A barra do Editor (#394): uma linha só, filtros à esquerda e ações à direita, Traduzir com IA sempre visível e as outras três iguais entre si.</summary>
public sealed class EditorToolbarTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData(960, true)]
    [InlineData(1280, true)]
    [InlineData(1366, true)]
    [InlineData(1440, false)]
    [InlineData(1920, false)]
    public async Task A_barra_fica_numa_linha_com_os_filtros_a_esquerda_e_as_acoes_a_direita(int windowWidth, bool iconsOnly)
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

                var header = window.GetVisualDescendants().OfType<PageHeader>().Single();
                var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(c => c.IsEffectivelyVisible).ToList();
                var tools = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tool") && !b.Classes.Contains("scenes-trigger") && b.IsEffectivelyVisible).ToList();
                var translate = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.TranslatePendingCommand);

                Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
                var all = chips.Cast<Control>().Concat(tools).Append(translate).ToList();

                if (tools.Count != 3)
                {
                    failures.Add($"esperava 3 botões secundários, achei {tools.Count}");
                }

                if (chips.Count != 5 || chips.Any(c => !c.IsEffectivelyVisible))
                {
                    failures.Add("os 5 filtros precisam estar visíveis");
                }

                if (all.Select(c => Math.Round(Box(c).Y)).Distinct().Count() != 1)
                {
                    failures.Add("filtros e ações não estão na mesma linha: " + string.Join(",", all.Select(c => Math.Round(Box(c).Y))));
                }

                if (all.Select(c => Math.Round(Box(c).Height)).Distinct().Count() != 1)
                {
                    failures.Add("filtros e ações não têm a mesma altura: " + string.Join(",", all.Select(c => Math.Round(Box(c).Height))));
                }

                if (Box(translate).Right > window.ClientSize.Width - 23 || Box(translate).Right < window.ClientSize.Width - 25)
                {
                    failures.Add($"Traduzir com IA deve encostar na margem direita ({Box(translate).Right:0} de {window.ClientSize.Width:0})");
                }

                if (chips.Max(c => Box(c).Right) > tools.Min(t => Box(t).X))
                {
                    failures.Add("os filtros passam por cima das ações");
                }

                if (!translate.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Traduzir com IA"))
                {
                    failures.Add("Traduzir com IA perdeu o texto");
                }

                var labels = tools.Select(t => t.GetVisualDescendants().OfType<TextBlock>().Single().IsEffectivelyVisible).ToList();
                if (labels.Any(shown => shown == iconsOnly))
                {
                    failures.Add($"rótulos dos botões secundários: {string.Join(",", labels)} (só ícones esperado: {iconsOnly})");
                }

                if (!iconsOnly && tools.Select(t => Math.Round(Box(t).Height)).Distinct().Count() != 1)
                {
                    failures.Add("os botões secundários têm alturas diferentes");
                }

                if (iconsOnly)
                {
                    if (tools.Select(t => Math.Round(Box(t).Width)).Distinct().Count() != 1)
                    {
                        failures.Add("os botões de ícone têm larguras diferentes");
                    }

                    if (tools.Any(t => string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(t)) || ToolTip.GetTip(t) is null))
                    {
                        failures.Add("botão de ícone sem nome ou sem dica");
                    }
                }

                // Sem o vazio de antes: a barra começa logo abaixo do título (a migalha e o título ocupam cerca de 56 px).
                var barTop = Box(chips[0]).Y;
                if (barTop - Box(header).Bottom > 16)
                {
                    failures.Add($"espaço vazio entre o título e a barra: {barTop - Box(header).Bottom:0} px");
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
