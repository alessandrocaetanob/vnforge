using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Claude;
using VNForge.AI.Explain;
using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using AvaloniaEdit;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O inspetor do Editor da #305 a 1440×1000 nos dois temas, com "Entender o japonês" aberto: <c>editor-inspetor-escuro.png</c> e <c>editor-inspetor-claro.png</c>
/// em <c>VNFORGE_RENDER_DIR</c>, para pôr ao lado do artboard. Confere a tinta dentro de retângulos conhecidos, não faixas de pixels de texto (a fonte muda no Linux).
/// </summary>
public sealed class EditorInspectorRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_inspetor_com_a_explicacao_a_cena_e_a_caixa_do_jogo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var editor = new EditorViewModel(scriptLabel: DemoProject.Label) { Explanations = new ExplicacaoDeExemplo() };
                var vm = new MainWindowViewModel(start) { Editor = editor };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");
                    await editor.Detail!.ToggleExplainCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-inspetor", theme)));
                    ConferirInspetor(window, editor);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void ConferirInspetor(Window window, EditorViewModel editor)
    {
        var detail = editor.Detail!;
        Assert.DoesNotContain("\\n", detail.FieldText, StringComparison.Ordinal);
        Assert.DoesNotContain("\\p", detail.FieldText, StringComparison.Ordinal);

        var view = window.GetVisualDescendants().OfType<LineDetailView>().Single();
        var campo = view.GetVisualDescendants().OfType<TextEditor>().Single(e => e.Name == "TranslationEditor");
        var caixa = view.GetVisualDescendants().OfType<GameTextbox>().Single();
        var cartao = view.GetVisualDescendants().OfType<ExplainCard>().Single();
        var cena = view.GetVisualDescendants().OfType<ContextLines>().Single();

        Assert.True(caixa.IsEffectivelyVisible && cartao.IsEffectivelyVisible && cena.IsEffectivelyVisible);
        Assert.Equal(2, cena.ItemCount);
        Assert.Equal(detail.ExplainParts.Count, cartao.ItemCount);

        var campoY = campo.TranslatePoint(default, window)!.Value.Y;
        var caixaY = caixa.TranslatePoint(default, window)!.Value.Y;
        Assert.True(caixaY > campoY + campo.Bounds.Height - 1, "a caixa do jogo fica logo abaixo do campo da tradução");
        Assert.True(caixaY - (campoY + campo.Bounds.Height) < 40, "e não a mais de uma linha de distância dele");

        var dentro = new Rect(view.TranslatePoint(default, window)!.Value, view.Bounds.Size);
        Assert.True(dentro.Contains(new Rect(caixa.TranslatePoint(default, window)!.Value, caixa.Bounds.Size)), "a caixa cabe dentro do inspetor");

        var probe = PixelProbe.Capture(window, caixa);
        var fundo = probe.At(1, 1);
        foreach (var linha in caixa.RowBounds().Where(l => l.Row.Kind == GameTextboxRowKind.Text))
        {
            Assert.True(TintaDiferenteDoFundo(probe, linha.Bounds, fundo) >= 20, $"linha {linha.Row.Line} da caixa sem tinta");
        }
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
