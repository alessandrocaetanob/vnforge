using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using AvaloniaEdit;

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

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<(EditorViewModel Vm, TempFolder Temp)> Abrir(ILineSuggester? suggester = null, bool twoScripts = false)
    {
        var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated, TranslationStatus.Translated);
        if (twoScripts)
        {
            var folder = ProjectFolder.Open(root);
            folder.SaveText("yst00002", [.. folder.LoadText("yst00001").Select(u => u with { Id = u.Id.Replace("yst00001", "yst00002", StringComparison.Ordinal) })]);
        }

        var vm = new EditorViewModel(postToUi: acao => acao(), suggester: suggester);
        vm.Project = EditorFixtures.Summary("Minha VN", root);
        await vm.Loading;
        vm.SelectedLine = vm.Lines[1];
        return (vm, temp);
    }

    /// <summary>Uma janela da largura pedida: o Editor decide o layout pela largura do cliente da janela.</summary>
    private static Window Show(Control content, double windowWidth)
    {
        var window = new Window { Width = windowWidth, Height = 768, Content = content };
        window.Show();
        Settle(window);
        return window;
    }

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
                    window.Width = width;
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

    [Theory]
    [InlineData(1440, false)]
    [InlineData(1400, false)]
    [InlineData(1399, true)]
    [InlineData(1366, true)]
    public async Task Na_MainWindow_o_layout_segue_a_largura_da_janela(int windowWidth, bool compact)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch));
        var main = new MainWindowViewModel(start);
        await start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        bool tabs = !compact, flyout = !compact;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new MainWindow { Width = windowWidth, Height = 768, DataContext = main };
                window.Show();
                main.SelectedNav = main.Nav.Single(n => n.Key == NavKey.Editor);
                Settle(window);
                tabs = window.GetVisualDescendants().OfType<TabControl>().Any(t => t.IsEffectivelyVisible);
                flyout = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger")).IsEffectivelyVisible;
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal(compact, tabs);
        Assert.Equal(compact, flyout);
    }

    [Fact]
    public async Task Cruzar_os_1400_px_e_voltar_mantem_o_mesmo_campo_com_texto_cursor_selecao_e_foco()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var states = new List<(bool Same, string? Text, int Caret, int Selection, bool Focused)>();
        var initial = (Text: string.Empty, Caret: 0, Selection: 0);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1440);
                var detailView = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                var editor = detailView.GetVisualDescendants().OfType<TextEditor>().Single();
                editor.Text = "uma tradução editada";
                editor.Select(4, 7);
                editor.CaretOffset = 11;
                editor.TextArea.Focus();
                Settle(window);
                initial = (editor.Text, editor.CaretOffset, editor.SelectionLength);
                Assert.True(editor.TextArea.IsFocused, "o campo começa com o foco");

                foreach (var width in new[] { 1399.0, 1440.0 })
                {
                    window.Width = width;
                    Settle(window);
                    var now = detailView.GetLogicalDescendants().OfType<TextEditor>().Distinct().Single();
                    states.Add((ReferenceEquals(now, editor), now.Text, now.CaretOffset, now.SelectionLength, now.TextArea.IsFocused));
                }

                window.Close();
            },
            CancellationToken.None);

        Assert.Equal("uma tradução editada", initial.Text);
        Assert.Equal(2, states.Count);
        Assert.All(states, st =>
        {
            Assert.True(st.Same);
            Assert.Equal(initial, (st.Text, st.Caret, st.Selection));
            Assert.True(st.Focused);
        });
    }

    private sealed class GatedSuggester : ILineSuggester
    {
        public TaskCompletionSource<LineSuggestion> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LineSuggestion> SuggestAsync(TextUnit unit, CancellationToken ct) => Gate.Task;
    }

    [Fact]
    public async Task Pedir_a_sugestao_leva_a_aba_Sugestao_uma_vez_e_a_resposta_nao_puxa_de_volta()
    {
        var gated = new GatedSuggester();
        var (vm, temp) = await Abrir(gated);
        using var _ = temp;
        var indexes = new List<int>();
        var focusInside = new List<bool>();

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1366);
                var detailView = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                var tabs = detailView.GetVisualDescendants().OfType<TabControl>().Single();
                var task = vm.Detail!.SuggestCommand.ExecuteAsync(null);
                Settle(window);
                indexes.Add(tabs.SelectedIndex);
                focusInside.Add(window.FocusManager?.GetFocusedElement() is Visual focused && detailView.IsVisualAncestorOf(focused));

                tabs.SelectedIndex = 0;
                Settle(window);
                gated.Gate.SetResult(new LineSuggestion("Texto sugerido", FromFakeProvider: true));
                await task;
                Settle(window);
                indexes.Add(tabs.SelectedIndex);
                Assert.True(vm.Detail.HasSuggestion);

                tabs.SelectedIndex = 1;
                Settle(window);
                var use = detailView.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UseSuggestionButton");
                Assert.True(use.Command!.CanExecute(null));
                use.Command.Execute(null);
                use.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Settle(window);
                Assert.Equal("Texto sugerido", vm.Detail.FieldText);
                indexes.Add(tabs.SelectedIndex);
                focusInside.Add(detailView.GetVisualDescendants().OfType<TextEditor>().Single().TextArea.IsFocused);
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal([1, 0, 0], indexes);
        Assert.Equal([true, true], focusInside);
    }

    [Fact]
    public async Task Escolher_uma_cena_no_flyout_fecha_o_flyout_e_troca_a_cena()
    {
        var (vm, temp) = await Abrir(twoScripts: true);
        using var _ = temp;
        bool open = false, closed = false;
        var before = vm.SelectedScript?.Key;
        string? after = null;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1366);
                var trigger = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger"));
                var flyout = (Flyout)trigger.Flyout!;
                flyout.ShowAt(trigger);
                Settle(window);
                open = flyout.IsOpen;
                var list = ((ScenesPanel)flyout.Content!).GetVisualDescendants().OfType<ListBox>().Single();
                list.SelectedIndex = list.SelectedIndex == 0 ? 1 : 0;
                Settle(window);
                closed = !flyout.IsOpen;
                after = vm.SelectedScript?.Key;
                window.Close();
            },
            CancellationToken.None);

        Assert.True(open);
        Assert.True(closed);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task O_flyout_aberto_fecha_quando_a_janela_cresce_alem_dos_1400_px()
    {
        var (vm, temp) = await Abrir();
        using var _ = temp;
        var open = new List<bool>();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(new EditorView { DataContext = vm }, 1366);
                var trigger = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger"));
                var flyout = (Flyout)trigger.Flyout!;
                flyout.ShowAt(trigger);
                Settle(window);
                open.Add(flyout.IsOpen);
                window.Width = 1440;
                Settle(window);
                open.Add(flyout.IsOpen);
                window.Close();
            },
            CancellationToken.None);

        Assert.Equal([true, false], open);
    }
}
