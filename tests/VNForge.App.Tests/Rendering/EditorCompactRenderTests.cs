using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using VNForge.AI.Claude;
using VNForge.AI.Explain;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Editor da #314 em 1366×768 (lista de cenas no flyout, botões como ícones, inspetor em três abas de 360 px) e em 1440×1000 (as três colunas do artboard), nos dois temas.
/// Os PNGs vão para <c>VNFORGE_RENDER_DIR</c>: <c>editor-compacto-traducao</c>, <c>-sugestao</c> e <c>-contexto</c> e <c>editor-1440</c>. "Sem texto cortado" se prova pela medida do layout
/// (largura natural do texto contra o espaço que o bloco recebeu), nunca por faixas de pixels de texto, que mudam com a fonte do Linux.
/// </summary>
public sealed class EditorCompactRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private const int CompactWidth = 1366;
    private const int CompactHeight = 768;
    private const double Tolerance = 0.5;

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1366_por_768_o_inspetor_em_abas_cabe_sem_texto_cortado_e_com_a_caixa_do_jogo_inteira(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var (window, editor) = await Open(config, demo.Root, theme, CompactWidth, CompactHeight);
                try
                {
                    var inspector = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    var panel = inspector.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    var tabs = inspector.GetVisualDescendants().OfType<TabControl>().Single();

                    Assert.InRange(panel.Bounds.Width, EditorBreakpoints.InspectorTabsWidth - 1, EditorBreakpoints.InspectorTabsWidth + 1);
                    Assert.False(window.GetVisualDescendants().OfType<ScenesPanel>().First().IsEffectivelyVisible, "a lista de cenas sai da grade");
                    Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("scenes-trigger")).IsEffectivelyVisible);

                    foreach (var (index, name) in new[] { (0, "traducao"), (1, "sugestao"), (2, "contexto") })
                    {
                        tabs.SelectedIndex = index;
                        ScreenRenderer.Settle(window);
                        ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"editor-compacto-{name}", theme)));
                        SemTextoCortado(inspector, panel, name);
                    }

                    tabs.SelectedIndex = 0;
                    ScreenRenderer.Settle(window);
                    ConferirCaixaDoJogo(window, inspector, editor);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1440_por_1000_o_Editor_mantem_as_tres_colunas_e_o_inspetor_inteiro(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await Open(config, demo.Root, theme, ScreenRenderer.Width, ScreenRenderer.Height);
                try
                {
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-1440", theme)));
                    var inspector = window.GetVisualDescendants().OfType<LineDetailView>().Single();
                    Assert.True(window.GetVisualDescendants().OfType<ScenesPanel>().First().IsEffectivelyVisible);
                    Assert.DoesNotContain(inspector.GetVisualDescendants().OfType<TabControl>(), t => t.IsEffectivelyVisible);
                    Assert.Equal(GameTextboxSize.Medium, inspector.GetVisualDescendants().OfType<GameTextbox>().Single().Size);
                    var panel = inspector.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));
                    Assert.InRange(panel.Bounds.Width, 419, 421);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<(MainWindow Window, EditorViewModel Editor)> Open(TempFolder config, string demoRoot, ThemeVariant theme, int width, int height)
    {
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoRoot), new FixedClock(Now));
        var suggester = new LineSuggester(() => new FakeTranslationProvider(u => (u.Translation ?? u.Source).Replace("Os dois", "Vocês dois")));
        var editor = new EditorViewModel(scriptLabel: DemoProject.Label, suggester: suggester) { Explanations = new ExplicacaoDeExemplo() };
        var vm = new MainWindowViewModel(start) { Editor = editor };
        var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");
        await editor.Detail!.SuggestCommand.ExecuteAsync(null);
        await editor.Detail!.ToggleExplainCommand.ExecuteAsync(null);
        ScreenRenderer.Settle(window);
        return (window, editor);
    }

    /// <summary>Todo bloco de texto visível do inspetor cabe no espaço que recebeu e dentro do painel; nenhum é cortado nem fica sob reticências.</summary>
    private static void SemTextoCortado(LineDetailView inspector, Border panel, string tab)
    {
        var clipped = ClippedTextHeadlessTests.FindClipped(inspector);
        Assert.True(clipped.Count == 0, $"aba {tab}: {string.Join("; ", clipped)}");

        var area = new Rect(panel.TranslatePoint(default, inspector)!.Value, panel.Bounds.Size).Inflate(Tolerance);
        foreach (var block in inspector.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.Bounds.Width > 0))
        {
            var bounds = new Rect(block.TranslatePoint(default, inspector)!.Value, block.Bounds.Size);
            Assert.True(bounds.X >= area.X && bounds.Right <= area.Right, $"aba {tab}: \"{block.Text}\" passa da borda do inspetor ({bounds.Right:0.#} de {area.Right:0.#})");

            if (block.TextTrimming != TextTrimming.None)
            {
                var natural = new TextBlock { Text = block.Text, FontFamily = block.FontFamily, FontSize = block.FontSize, FontWeight = block.FontWeight, FontStyle = block.FontStyle };
                natural.Measure(Size.Infinity);
                Assert.True(block.Bounds.Width + Tolerance >= natural.DesiredSize.Width, $"aba {tab}: \"{block.Text}\" ficou com reticências ({block.Bounds.Width:0.#} de {natural.DesiredSize.Width:0.#})");
            }
        }
    }

    private static void ConferirCaixaDoJogo(Window window, LineDetailView inspector, EditorViewModel editor)
    {
        var campo = inspector.GetVisualDescendants().OfType<TextEditor>().Single(e => e.Name == "TranslationEditor");
        var caixa = inspector.GetVisualDescendants().OfType<GameTextbox>().Single();
        var rolagem = caixa.GetVisualAncestors().OfType<ScrollViewer>().First();

        Assert.True(caixa.IsEffectivelyVisible);
        Assert.Equal(GameTextboxSize.Small, caixa.Size);
        Assert.Contains(inspector.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("kbd") && b.IsEffectivelyVisible);
        Assert.True(caixa.Bounds.Width <= rolagem.Viewport.Width + Tolerance, $"a caixa do jogo inteira cabe sem rolagem horizontal ({caixa.Bounds.Width:0.#} de {rolagem.Viewport.Width:0.#})");
        Assert.True(rolagem.Extent.Width <= rolagem.Viewport.Width + Tolerance, "sem rolagem horizontal na caixa");

        var campoY = campo.TranslatePoint(default, window)!.Value.Y;
        var caixaY = caixa.TranslatePoint(default, window)!.Value.Y;
        Assert.True(caixaY > campoY + campo.Bounds.Height - 1, "a caixa do jogo fica logo abaixo do campo da tradução");

        var dentro = new Rect(inspector.TranslatePoint(default, window)!.Value, inspector.Bounds.Size);
        Assert.True(dentro.Contains(new Rect(caixa.TranslatePoint(default, window)!.Value, caixa.Bounds.Size)), "a caixa cabe dentro do inspetor");

        var probe = PixelProbe.Capture(window, caixa);
        var fundo = probe.At(1, 1);
        foreach (var linha in caixa.RowBounds().Where(l => l.Row.Kind == GameTextboxRowKind.Text))
        {
            Assert.True(TintaDiferenteDoFundo(probe, linha.Bounds, fundo) >= 15, $"linha {linha.Row.Line} da caixa sem tinta");
        }

        Assert.NotEmpty(editor.Detail!.FieldText);
    }

    private static int TintaDiferenteDoFundo(PixelProbe probe, Rect area, Color fundo)
    {
        var diferentes = 0;
        for (var y = (int)area.Y; y < (int)area.Bottom && y < probe.Height; y++)
        {
            for (var x = (int)area.X; x < (int)area.Right && x < probe.Width; x++)
            {
                if (!probe.Near(x, y, fundo, 40))
                {
                    diferentes++;
                }
            }
        }

        return diferentes;
    }

    private sealed class ExplicacaoDeExemplo : ILineExplanations
    {
        public bool IsAvailable => true;

        public string PriceText => "menos de R$ 0,01";

        public Task<LineExplanation> ExplainAsync(string projectRoot, string source, CancellationToken ct) =>
            Task.FromResult(new LineExplanation(
                [new ExplainedPart("今日は", "hoje (tema da frase)"), new ExplainedPart("雨が", "a chuva (sujeito)"), new ExplainedPart("降っている", "está caindo")],
                ClaudeModels.Haiku,
                FromCache: false));
    }
}
