using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Glossary;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Glossário da interface v3 a 1440×1000 nos dois temas: Personagens com o gênero que falta em destaque, e Termos com o painel "Termos que se repetem"
/// já traduzido pela IA falsa. Confere a tinta dentro de retângulos conhecidos (a borda de aviso do seletor, a posição dos botões), não faixas de texto,
/// porque a fonte do CI é outra. Os PNG seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class GlossaryV3RenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 20, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static string CreateProject(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        List<TextUnit> units =
        [
            Line(0, "「先輩、遅刻です」", "美咲"),
            Line(1, "遅いぞ", "健太"), Line(2, "静かにしなさい", "先生"),
            Line(3, "早くしろ", "健太"), Line(4, "行くぞ", "健太"),
            Line(5, "席に着きなさい", "先生"), Line(6, "宿題を出しなさい", "先生"),
        ];
        units.AddRange(Enumerable.Range(10, 9).Select(n => Line(n, $"魔法学院の噂その{n}")));
        units.AddRange(Enumerable.Range(30, 7).Select(n => Line(n, $"生徒会長の命令その{n}")));
        units.AddRange(Enumerable.Range(50, 5).Select(n => Line(n, $"ラムネを買った{n}")));
        folder.SaveText("yst00001", units);
        folder.SaveGlossary([new GlossaryTerm("先輩", "veterano", "tratamento")]);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
        return root;
    }

    private static Color Brush(Window window, string key, ThemeVariant theme) =>
        window.TryFindResource(key, theme, out var resource) && resource is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"Recurso {key} não encontrado.");

    private static int Edge(PixelProbe probe, Color color) =>
        Enumerable.Range(0, 3).Sum(y => probe.CountInRow(y, color, 6, 0, probe.Width));

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Personagens_destaca_o_seletor_de_genero_vazio_e_oferece_a_sugestao(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var project = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(CreateProject(project)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                vm.Glossary.Suggester = new FakeGlossarySuggester();
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;
                    ScreenRenderer.Settle(window);

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v3-personagens", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    var selectors = window.GetVisualDescendants().OfType<ComboBox>().ToList();
                    var empty = Assert.Single(selectors, c => c.Classes.Contains("needs"));
                    var filled = Assert.Single(selectors, c => !c.Classes.Contains("needs") && c.IsVisible);
                    var warning = Brush(window, "Vnf.SignalWarningFg", theme);

                    Assert.True(Edge(PixelProbe.Capture(window, empty), warning) > empty.Bounds.Width / 2, "a borda do seletor vazio deveria ter o tom de aviso");
                    Assert.Equal(0, Edge(PixelProbe.Capture(window, filled), warning));

                    var banner = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("infobar") && b.Classes.Contains("warning") && b.IsVisible);
                    Assert.True(banner.Bounds.Height > 20 && banner.Bounds.Right <= ScreenRenderer.Width);

                    var suggest = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.SuggestVoicesCommand);
                    Assert.True(suggest.IsEffectivelyEnabled);
                    var corner = suggest.TranslatePoint(new Point(suggest.Bounds.Width, suggest.Bounds.Height), window)!.Value;
                    Assert.True(corner.X <= ScreenRenderer.Width && corner.Y <= ScreenRenderer.Height, "o botão de sugestão cabe na janela");
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
    public async Task Termos_mostra_o_painel_dos_que_se_repetem_com_a_traducao_e_o_botao_Adicionar(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var project = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(CreateProject(project)), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                vm.Glossary.Suggester = new FakeGlossarySuggester(term: t => new TermTranslationSuggestion(t.Source, $"tradução de {t.Lines}", "Aparece em todo o jogo."));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;
                    vm.Glossary.SelectedTab = GlossaryTab.Terms;
                    await vm.Glossary.SuggestTermsCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v3-termos", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    Assert.Equal(["魔法学院", "命令", "生徒会長", "ラムネ"], vm.Glossary.RepeatedTermRows.Select(r => r.Source));
                    var add = window.GetVisualDescendants().OfType<Button>().Where(b => b.Command == vm.Glossary.AddRepeatedTermCommand && b.IsVisible).ToList();
                    Assert.Equal(4, add.Count);
                    foreach (var button in add)
                    {
                        var origin = button.TranslatePoint(default, window)!.Value;
                        Assert.True(origin.X > ScreenRenderer.Width / 2.0 && origin.X + button.Bounds.Width <= ScreenRenderer.Width, "o painel fica à direita da tabela, dentro da janela");
                    }

                    var panel = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("panel") && b.Bounds.Width is > 340 and < 380);
                    Assert.True(panel.Bounds.Height > add.Count * 40, "cada termo ocupa a sua linha no painel");
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
