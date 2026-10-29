using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Editor com o maior tamanho do texto (150%, o que o Ctrl + alcança): a grade nas duas densidades, o inspetor e o Modo revisão, sem texto cortado
/// e com cada linha da grade envolvendo o que escreve. Os PNG (<c>VNFORGE_RENDER_DIR</c>) mostram o resultado.
/// </summary>
public sealed class EditorTextSizeRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly string Long = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";

    public static TheoryData<int, int> Windows => new() { { 1440, 1000 }, { 1366, 768 } };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task Grade_com_o_maior_tamanho_nao_corta_texto_nem_estoura_a_linha(int width, int height)
    {
        await Abrir(width, height, (window, vm) =>
        {
            Assert.Equal(150, vm.Settings.TextScalePercent);
            AssertGradeSemCorte(window);

            vm.Editor.IsScriptDensity = true;
            ScreenRenderer.Settle(window);
            AssertGradeSemCorte(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-texto-150-roteiro-{width}.png"));
        });
    }

    public static TheoryData<int, int, int> InspectorCases
    {
        get
        {
            var data = new TheoryData<int, int, int>();
            foreach (var percent in new[] { 100, 110, 125, 150 })
            {
                data.Add(1440, 1000, percent);
                data.Add(1366, 768, percent);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InspectorCases))]
    public async Task Inspetor_em_cada_tamanho_nao_corta_texto(int width, int height, int percent)
    {
        await Abrir(width, height, (window, vm) =>
        {
            Assert.NotNull(vm.Editor.Detail);
            var inspector = Assert.Single(window.GetVisualDescendants().OfType<LineDetailView>(), v => v.IsEffectivelyVisible);
            ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-texto-{percent}-inspetor-{width}.png"));
            // O inspetor rola (a coluna passa da altura a 1366x768 com a tradução longa): o corte aceito é o da rolagem. O que não rola, as linhas sem quebra, não pode estar cortado.
            Assert.Empty(ClippedTextHeadlessTests.FindClipped(inspector));
            var translation = inspector.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("jp-body"));
            Assert.Equal(Math.Round(15 * percent / 100d, 1), translation.FontSize);
        }, percent);
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task Modo_revisao_com_o_maior_tamanho_nao_corta_texto(int width, int height)
    {
        await Abrir(width, height, (window, vm) =>
        {
            var entry = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.StartReviewCommand);
            entry.Command!.Execute(null);
            ScreenRenderer.Settle(window);
            Assert.True(vm.Editor.IsReviewing);

            var review = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            Assert.Empty(ClippedTextHeadlessTests.FindClipped(review));
            // O roteiro rola sozinho: a fala a aprovar tem que caber inteira no viewport dele, com a fala anterior e a seguinte escondidas se for preciso.
            var script = review.FindControl<ScrollViewer>("Script")!;
            var card = review.FindControl<Border>("TranslationCard")!;
            var translated = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == vm.Editor.Review!.Detail!.FieldText);
            Assert.Equal(30d, translated.FontSize);
            Assert.True(translated.Bounds.Height >= translated.LineHeight, "a tradução ocupa ao menos uma linha inteira");
            var top = translated.TranslatePoint(default, script)!.Value.Y;
            var bottom = top + translated.Bounds.Height;
            Assert.True(top >= -0.5 && bottom <= script.Viewport.Height + 0.5,
                $"a tradução ({top:0.#} a {bottom:0.#}) passa do viewport do roteiro ({script.Viewport.Height:0.#})");
            var cardTop = card.TranslatePoint(default, script)!.Value.Y;
            Assert.True(cardTop >= -0.5 && cardTop + card.Bounds.Height <= script.Viewport.Height + 0.5, "o cartão da tradução cabe inteiro no viewport");
            ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-texto-150-revisao-{width}.png"));
        });
    }

    private static void AssertGradeSemCorte(Window window)
    {
        var grid = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("lines"));
        var rows = grid.GetVisualDescendants().OfType<LineRow>().Where(r => r.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Empty(ClippedTextHeadlessTests.FindClipped(row));
            foreach (var block in row.GetVisualDescendants().OfType<TextBlock>().Where(b => b.IsEffectivelyVisible && !string.IsNullOrEmpty(b.Text)))
            {
                var origin = block.TranslatePoint(default, row) ?? throw new InvalidOperationException("O texto não está na linha.");
                Assert.True(origin.Y >= -0.5 && origin.Y + block.Bounds.Height <= row.Bounds.Height + 0.5,
                    $"\"{block.Text}\" ({origin.Y:0.#} a {origin.Y + block.Bounds.Height:0.#}) passa da linha de {row.Bounds.Height:0.#}");
            }
        }
    }

    private async Task Abrir(int width, int height, Action<MainWindow, MainWindowViewModel> verificar, int percent = 150)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Machine, TranslationStatus.Approved);
                var folder = ProjectFolder.Open(root);
                var units = folder.LoadText("yst00001").ToList();
                units[0] = units[0] with { Source = "担任の先生が腕を組んで立っていた。", Translation = Long, Speaker = "健太" };
                units[1] = units[1] with { Translation = Long + " " + Long };
                folder.SaveText("yst00001", units);

                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), TimeProvider.System);
                var vm = new MainWindowViewModel(start, settings: new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("app")), _ => null));
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Dark, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    ScreenRenderer.Settle(window);
                    while (vm.Settings.TextScalePercent < percent)
                    {
                        vm.Settings.IncreaseTextSizeCommand.Execute(null);
                    }

                    ScreenRenderer.Settle(window);
                    verificar(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
