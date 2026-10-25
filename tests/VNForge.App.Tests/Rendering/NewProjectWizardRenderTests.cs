using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Os quatro passos do assistente de novo projeto da interface v3 (#312), a 1440×1000 nos dois temas, para pôr ao lado do artboard no PR.
/// Além do PNG, confere a tinta dentro de retângulos conhecidos (a caixa de cada cartão de acentos) e os textos visíveis de cada passo.
/// Os PNGs saem em <c>VNFORGE_RENDER_DIR</c> como <c>novo-projeto-v3-N-passo-tema.png</c>.
/// </summary>
public sealed class NewProjectWizardRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Ink = 24;

    private const int MinimumInk = 3;

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private const string PlainForbidden = "YPF|XOR|932|0x[0-9A-F]|ysbin|Shift-JIS|(?i:token)|(?i:opus|sonnet|haiku|fable)|claude-|ANTHROPIC|DPAPI";

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Cada_passo_renderiza_com_a_tinta_no_lugar_e_sem_jargao_fora_dos_detalhes_tecnicos(string themeName)
    {
        var theme = Variant(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var game = temp.SubFolder("jogo");
                File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
                var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new PatchableEngine(312)], appSettings: new AppSettingsFile(temp.SubFolder("config")), clock: new FixedClock(Now));
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, newProjectWizard: () => wizard);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    start.NewProjectCommand.Execute(null);
                    Assert.Same(wizard, vm.CurrentPage);

                    await wizard.SetGameFolderAsync(game);
                    Render(window, "novo-projeto-v3-1-jogo", theme);
                    CheckGamePlain(window);

                    wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.ProjectName = "Minha VN";
                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-v3-3-acentos", theme);
                    CheckAccents(window);

                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.RateText = "5,40";
                    wizard.LimitText = "300";
                    Render(window, "novo-projeto-v3-4-ia-e-gasto", theme);
                    CheckAi(window, wizard);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Render(MainWindow window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    private static string[] VisibleTexts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!)];

    private static void CheckGamePlain(Window window)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Feito com o motor YU-RIS", texts);
        Assert.Contains("312 arquivos de roteiro", texts);
        Assert.All(texts, text => Assert.DoesNotMatch(PlainForbidden, text));
    }

    private static void CheckAccents(Window window)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Sem acentos", texts);
        Assert.Contains("Com acentos, fonte modificada", texts);
        Assert.Contains("Com acentos, ajuste no jogo", texts);
        Assert.Contains("Em breve", texts);
        Assert.Contains("Recomendada para este jogo", texts);
        Assert.All(texts, text => Assert.DoesNotMatch(PlainForbidden, text));

        var cards = window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "WizardAccents").ToArray();
        Assert.Equal([true, false, true], cards.Select(c => c.IsEnabled));

        var boxes = window.GetVisualDescendants().OfType<GameTextbox>().ToArray();
        Assert.Equal(3, boxes.Length);
        foreach (var box in boxes)
        {
            var ink = Token(box, "Vnf.TextboxInk");
            var probe = PixelProbe.Capture(window, box);
            var lines = box.RowBounds().Where(r => r.Row.Kind == GameTextboxRowKind.Text).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.True(probe.CountInArea(line.Bounds, ink, Ink) >= MinimumInk, "a linha da frase de teste tem tinta"));
        }
    }

    private static void CheckAi(Window window, NewProjectWizardViewModel wizard)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Rápida", texts);
        Assert.Contains("Equilibrada", texts);
        Assert.Contains("Caprichada", texts);
        Assert.All(texts, text => Assert.DoesNotMatch(PlainForbidden, text));

        var cards = window.GetVisualDescendants().OfType<QualityCard>().ToArray();
        Assert.Equal(["Rápida", "Equilibrada", "Caprichada"], cards.Select(c => c.Title));
        Assert.All(cards, card => Assert.StartsWith("R$", card.PriceText));
        Assert.Equal(wizard.Qualities.Select(q => q.Price), cards.Select(c => c.Price));

        var fields = window.GetVisualDescendants().OfType<TextBox>().ToDictionary(t => Avalonia.Automation.AutomationProperties.GetName(t) ?? string.Empty, t => t.Text);
        Assert.Equal("5,40", fields["Cotação do dólar em reais"]);
        Assert.Equal("300", fields["Limite de gasto em reais"]);
    }

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), key);
        return Assert.IsType<ISolidColorBrush>(found, exactMatch: false).Color;
    }

    /// <summary>Uma engine que se diz YU-RIS com <paramref name="scripts"/> scripts e oferece o patch do executável.</summary>
    private sealed class PatchableEngine(int scripts) : IEngine, IGamePatchProvider
    {
        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("parece fake", new Dictionary<string, string>
        {
            ["ypfVersion"] = "500",
            ["scriptKey"] = "0xD36FAC96",
            ["scriptsArchive"] = "ysbin.ypf",
            ["scripts"] = scripts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public IReadOnlyList<IGamePatch> CreatePatches(string executable) => [];
    }
}
