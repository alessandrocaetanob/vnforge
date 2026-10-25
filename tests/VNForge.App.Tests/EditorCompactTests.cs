using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O Editor compacto da #314: os pontos de quebra sem janela e o layout montado a 1366 e a 1440 px.</summary>
public sealed class EditorCompactTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Theory]
    [InlineData(1366, true)]
    [InlineData(1399, true)]
    [InlineData(1400, false)]
    [InlineData(1440, false)]
    [InlineData(1920, false)]
    public void Lista_de_cenas_vira_flyout_abaixo_de_1400(double windowWidth, bool flyout) =>
        Assert.Equal(flyout, EditorBreakpoints.UsesSceneFlyout(windowWidth));

    [Theory]
    [InlineData(1366, true)]
    [InlineData(1399, true)]
    [InlineData(1440, false)]
    [InlineData(1920, false)]
    public void Inspetor_vira_abas_na_janela_pequena(double windowWidth, bool tabs) =>
        Assert.Equal(tabs, EditorBreakpoints.UsesInspectorTabs(windowWidth));

    [Fact]
    public void A_largura_da_janela_soma_o_trilho_ao_Editor()
    {
        Assert.Equal(1440, EditorBreakpoints.WindowWidth(1367));
        Assert.Equal(1366, EditorBreakpoints.WindowWidth(1293));
    }

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

    /// <summary>Uma janela do tamanho do app: o Editor recebe a largura menos o trilho.</summary>
    private static Window Show(Control content, double windowWidth)
    {
        var window = new Window { Width = EditorWidth(windowWidth), Height = 768, Content = content };
        window.Show();
        Settle(window);
        return window;
    }

    private static double EditorWidth(double windowWidth) => windowWidth - EditorBreakpoints.WindowChrome;

    [Fact]
    public async Task A_1366_a_lista_de_cenas_sai_da_grade_e_abre_num_flyout_do_cabecalho()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        bool panelShown = true, triggerShown = false, flyoutHasScenes = false;
        double inspector = 0;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1366);
                var scenes = window.GetVisualDescendants().OfType<ScenesPanel>().First();
                panelShown = scenes.IsEffectivelyVisible;
                var trigger = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger"));
                triggerShown = trigger.IsEffectivelyVisible;
                var flyout = Assert.IsType<Flyout>(trigger.Flyout);
                flyout.ShowAt(trigger);
                Settle(window);
                flyoutHasScenes = flyout.Content is ScenesPanel { DataContext: EditorViewModel } panel
                    && panel.GetVisualDescendants().OfType<ListBox>().Single().ItemCount == vm.Scripts.Count;
                flyout.Hide();
                inspector = window.GetVisualDescendants().OfType<LineDetailView>().Single().GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel")).Bounds.Width;
                window.Close();
            },
            CancellationToken.None);

        Assert.False(panelShown);
        Assert.True(triggerShown);
        Assert.True(flyoutHasScenes);
        Assert.InRange(inspector, EditorBreakpoints.InspectorTabsWidth - 1, EditorBreakpoints.InspectorTabsWidth + 1);
    }

    [Fact]
    public async Task A_1440_a_lista_de_cenas_fica_na_grade_e_o_inspetor_nao_tem_abas()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        bool panelShown = false, triggerShown = true, tabsShown = true;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1440);
                panelShown = window.GetVisualDescendants().OfType<ScenesPanel>().First().IsEffectivelyVisible;
                triggerShown = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger")).IsEffectivelyVisible;
                tabsShown = window.GetVisualDescendants().OfType<TabControl>().Any(t => t.IsEffectivelyVisible);
                window.Close();
            },
            CancellationToken.None);

        Assert.True(panelShown);
        Assert.False(triggerShown);
        Assert.False(tabsShown);
    }

    [Fact]
    public async Task Os_botoes_secundarios_do_cabecalho_viram_icones_com_nome_e_dica()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var compact = new List<(string? Name, object? Tip, bool LabelShown)>();
        var wide = new List<bool>();

        await fixture.Session.Dispatch(
            () =>
            {
                foreach (var width in new[] { 1366, 1440 })
                {
                    var window = Show(new EditorView { DataContext = vm }, width);
                    foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Command == vm.ToggleSearchCommand || b.Command == vm.CheckFileCommand))
                    {
                        var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                        if (width == 1366)
                        {
                            compact.Add((AutomationProperties.GetName(button), ToolTip.GetTip(button), label.IsEffectivelyVisible));
                        }
                        else
                        {
                            wide.Add(label.IsEffectivelyVisible);
                        }
                    }

                    window.Close();
                }
            },
            CancellationToken.None);

        Assert.Equal(2, compact.Count);
        Assert.All(compact, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name));
            Assert.NotNull(b.Tip);
            Assert.False(b.LabelShown);
        });
        Assert.Equal([true, true], wide);
    }

    [Fact]
    public async Task A_1366_o_inspetor_tem_tres_abas_com_as_mesmas_funcoes_ligadas_ao_mesmo_ViewModel()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var headers = new List<string>();
        var placement = new Dictionary<string, int>();
        var commands = new List<object?>();
        GameTextboxSize boxSize = default;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1366);
                var detailView = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                var tabs = detailView.GetVisualDescendants().OfType<TabControl>().Single();
                var items = tabs.Items.OfType<TabItem>().ToList();
                headers.AddRange(items.Select(t => (string)t.Header!));
                for (var i = 0; i < items.Count; i++)
                {
                    var content = (Control)items[i].Content!;
                    foreach (var name in new[] { "TranslationEditor", "ExplainCard", "SceneLines", "ContextFragment", "SuggestionCard", "GameBox", "ApproveButton", "UseSuggestionButton" })
                    {
                        if (content.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == name))
                        {
                            placement[name] = i;
                        }
                    }
                }

                commands.AddRange(detailView.GetLogicalDescendants().OfType<Button>().Select(b => b.Command));
                boxSize = detailView.GetLogicalDescendants().OfType<GameTextbox>().Distinct().Single().Size;
                Assert.Same(vm.Detail, detailView.DataContext);
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal(["Tradução", "Sugestão", "Contexto"], headers);
        Assert.Equal(0, placement["TranslationEditor"]);
        Assert.Equal(0, placement["GameBox"]);
        Assert.Equal(0, placement["ApproveButton"]);
        Assert.Equal(1, placement["SuggestionCard"]);
        Assert.Equal(1, placement["UseSuggestionButton"]);
        Assert.Equal(2, placement["ExplainCard"]);
        Assert.Equal(2, placement["SceneLines"]);
        Assert.Equal(2, placement["ContextFragment"]);
        Assert.Equal(GameTextboxSize.Small, boxSize);
        Assert.Contains(vm.Detail!.ApproveAndNextCommand, commands);
        Assert.Contains(vm.Detail.AcceptSuggestionCommand, commands);
        Assert.Contains(vm.Detail.UseForAllCommand, commands);
        Assert.Contains(vm.Detail.SuggestCommand, commands);
    }

    [Fact]
    public async Task Redimensionar_a_janela_troca_entre_o_inspetor_inteiro_e_as_abas_sem_perder_o_texto()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var states = new List<(bool Tabs, string? Text, GameTextboxSize Size)>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1440);
                var detailView = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                foreach (var width in new[] { 1440.0, 1366.0, 1440.0 })
                {
                    window.Width = EditorWidth(width);
                    Settle(window);
                    var editor = detailView.GetLogicalDescendants().OfType<AvaloniaEdit.TextEditor>().Distinct().Single();
                    var tabs = detailView.GetVisualDescendants().OfType<TabControl>().Any(t => t.IsEffectivelyVisible);
                    states.Add((tabs, editor.Text, detailView.GetLogicalDescendants().OfType<GameTextbox>().Distinct().Single().Size));
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.Equal([false, true, false], states.Select(s => s.Tabs));
        Assert.All(states, s => Assert.Equal(vm.Detail!.FieldText, s.Text));
        Assert.Equal([GameTextboxSize.Medium, GameTextboxSize.Small, GameTextboxSize.Medium], states.Select(s => s.Size));
    }
}
