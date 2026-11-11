using Avalonia;
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

    private static readonly string[] TabbedNames = ["TranslationEditor", "ExplainCard", "SceneLines", "ContextFragment", "SuggestionCard", "GameBox", "ApproveButton", "UseSuggestionButton"];
    private static readonly double[] ResizeSteps = [1440.0, 1366.0, 1440.0];
    private static readonly double[] CrossingSteps = [1399.0, 1440.0];

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
}
