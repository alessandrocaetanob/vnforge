using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Glossary;
using VNForge.App.Controls;
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
        folder.SaveGlossary(
        [
            new GlossaryTerm("遅刻", "atraso"),
            new GlossaryTerm("傘", "guarda-chuva"),
            new GlossaryTerm("担任の先生", "professor responsável"),
            new GlossaryTerm("校門", "portão da escola"),
        ]);
        folder.SaveCharacters(
        [
            new CharacterEntry("美咲", "Misaki", Tone: "fala informal e animada; chama o Kenta", Color: "rose", Description: "Secundária · Professora, rígida"),
            new CharacterEntry("健太", "Kenta", Tone: "seco e resmungão; frases curtas", Color: "teal"),
            new CharacterEntry("陽菜", "Hina", Color: "violet"),
            new CharacterEntry("蓮", "Ren", Color: "orange"),
            new CharacterEntry("葵", "Aoi", Color: "blue"),
            new CharacterEntry("先生", "Professor", Tone: "formal; chama os alunos pelo sobrenome", Color: "green"),
            new CharacterEntry("健太の母", "Mãe do Kenta", Color: "red"),
            new CharacterEntry("脇役01", "Coadjuvante 01", Color: "teal"),
            new CharacterEntry("脇役02", "Coadjuvante 02", Color: "violet"),
            new CharacterEntry("脇役03", "Coadjuvante 03", Color: "blue"),
        ]);
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
    public async Task Personagens_deixa_o_seletor_vazio_neutro_e_oferece_Pedir_a_IA_na_faixa(string themeName)
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

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v6-personagens", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    AssertTabsRow(window, vm.Glossary, theme);
                    var characterCard = Card(window);
                    Assert.Equal(1296, characterCard.Bounds.Width, 1);
                    var characterRows = Rows(window);
                    Assert.Equal(10, characterRows.Count);
                    Assert.All(characterRows, r => Assert.Equal(47, r.Bounds.Height, 0.5));
                    var genders = Combos(window, "Gênero");
                    Assert.Equal(10, genders.Count);
                    Assert.All(genders, c => Assert.Equal(new Size(150, 28), c.Bounds.Size));
                    var colors = Combos(window, "Cor do personagem");
                    Assert.All(colors, c => Assert.Equal(new Size(112, 28), c.Bounds.Size));
                    Assert.Equal(10, window.GetVisualDescendants().OfType<Portrait>().Count(p => p.Size == PortraitSize.Sm && p.IsEffectivelyVisible));
                    Assert.All(window.GetVisualDescendants().OfType<Portrait>().Where(p => p.IsEffectivelyVisible), p => Assert.Equal(28, p.Bounds.Width));

                    var warning = Brush(window, "Vnf.SignalWarningFg", theme);
                    var selectors = window.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible).ToList();
                    Assert.NotEmpty(selectors);
                    Assert.All(selectors, c => Assert.Equal(0, Edge(PixelProbe.Capture(window, c), warning)));

                    var banner = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("infobar") && b.Classes.Contains("warning") && b.IsVisible);
                    Assert.True(banner.Bounds.Height > 20 && banner.Bounds.Right <= ScreenRenderer.Width);

                    var ask = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.SuggestVoicesCommand);
                    Assert.True(ask.IsEffectivelyEnabled && ask.Classes.Contains("accent"), "Pedir à IA é o botão principal da faixa");
                    Assert.True(new Rect(banner.Bounds.Size).Contains(ask.TranslatePoint(new Point(ask.Bounds.Width / 2, ask.Bounds.Height / 2), banner)!.Value), "o botão fica dentro da faixa");
                    var corner = ask.TranslatePoint(new Point(ask.Bounds.Width, ask.Bounds.Height), window)!.Value;
                    Assert.True(corner.X <= ScreenRenderer.Width && corner.Y <= ScreenRenderer.Height, "o botão de sugestão cabe na janela");
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.AcceptAllSuggestionsCommand && b.IsEffectivelyVisible);

                    await vm.Glossary.SuggestVoicesCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v6-genero-sugerido", theme)));

                    var suggested = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("suggested") && b.IsEffectivelyVisible).ToList();
                    Assert.Equal(vm.Glossary.PendingSuggestionCount, suggested.Count);
                    Assert.NotEmpty(suggested);
                    var acceptAll = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.AcceptAllSuggestionsCommand);
                    Assert.True(acceptAll.IsEffectivelyVisible && acceptAll.IsEffectivelyEnabled && acceptAll.Classes.Contains("accent"));
                    Assert.False(ask.Classes.Contains("accent"), "com sugestões esperando, o aceite passa a ser o botão principal");
                    var check = Assert.Single(suggested[0].GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.AcceptSuggestionCommand);
                    Assert.True(check.IsEffectivelyVisible && check.Bounds.Right <= suggested[0].Bounds.Width);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Capture(Window window, string name, ThemeVariant theme)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    private static List<ComboBox> Combos(Window window, string name) =>
        [.. window.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name)];

    private static List<Border> Rows(Window window) =>
        [.. window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("grow") && b.IsEffectivelyVisible)];

    private static Border Card(Window window) =>
        window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.Classes.Contains("card") && b.IsEffectivelyVisible);

    /// <summary>A linha das abas do quadro: busca de 300 x 32 e o botão comum de 32 de altura, os dois alinhados à direita, e as abas de texto com o contador.</summary>
    private static void AssertTabsRow(Window window, GlossaryViewModel glossary, ThemeVariant theme)
    {
        var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Buscar no glossário");
        Assert.Equal(new Size(300, 32), search.Bounds.Size);
        var add = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Command == glossary.AddCommand);
        Assert.Equal(32, add.Bounds.Height);
        Assert.False(add.Classes.Contains("accent"), "no quadro o Adicionar é o botão comum; a ação principal da tela é a da faixa");
        Assert.Equal(ScreenRenderer.Width - 32, add.TranslatePoint(new Point(add.Bounds.Width, 0), window)!.Value.X, 1);

        var tabs = window.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Equal(4, tabs.Count);
        Assert.All(tabs, t => Assert.Equal(43, t.Bounds.Height));
        Assert.All(tabs, t => Assert.Equal(15, t.FontSize));
        var count = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("count") && t.IsEffectivelyVisible);
        Assert.Equal(Brush(window, "Vnf.TextSecondary", theme), ((ISolidColorBrush)count.Foreground!).Color);
        // A contagem tem o corpo e a altura de linha do texto da aba: assim as duas linhas de base coincidem.
        Assert.Equal(15, count.FontSize);
        var label = ((StackPanel)count.Parent!).Children.OfType<TextBlock>().First();
        Assert.Equal(label.Bounds.Height, count.Bounds.Height, 0.5);
        Assert.Equal(label.Bounds.Y, count.Bounds.Y, 0.5);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Ficha_abre_dentro_do_cartao_com_o_retrato_de_56_e_o_botao_de_revisar(string themeName)
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
                    vm.Glossary.OpenSheetCommand.Execute(vm.Glossary.Characters[0]);
                    await vm.Glossary.SheetLoading;
                    ScreenRenderer.Settle(window);

                    var path = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-v6-ficha", theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }

                    var sheet = Assert.Single(window.GetVisualDescendants().OfType<CharacterSheetView>(), s => s.IsEffectivelyVisible);
                    var card = Card(window);
                    Assert.True(sheet.Bounds.Width >= card.Bounds.Width - 2, "a ficha ocupa a largura do cartão, sem fundo próprio");
                    var portrait = Assert.Single(sheet.GetVisualDescendants().OfType<Portrait>());
                    Assert.Equal(new Size(56, 56), portrait.Bounds.Size);
                    Assert.Equal(20, portrait.TranslatePoint(default, card)!.Value.X - 1, 1);

                    var title = sheet.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title-md"));
                    Assert.Equal(17, title.FontSize);
                    Assert.Equal(FontWeight.Bold, title.FontWeight);
                    Assert.Equal(CharacterPalette.Find("rose")!.BrushFor(theme), title.Foreground);
                    Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Secundária · Professora, rígida" && t.FontSize == 12);

                    var review = Assert.Single(sheet.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Revisar este personagem");
                    Assert.True(review.Classes.Contains("accent") && !review.Classes.Contains("sm") && review.Bounds.Height == 32);

                    var banner = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("infobar") && b.Classes.Contains("warning") && b.IsVisible);
                    Assert.True(banner.TranslatePoint(default, window)!.Value.Y > sheet.TranslatePoint(new Point(0, sheet.Bounds.Height), window)!.Value.Y - 1, "a faixa de gênero vem depois da ficha");
                    Assert.Equal(10, Rows(window).Count);
                    Assert.All(Rows(window), r => Assert.Equal(47, r.Bounds.Height, 0.5));
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
                    ScreenRenderer.Settle(window);
                    Capture(window, "glossario-v6-termos", theme);
                    var waiting = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("divider-top") && b.IsEffectivelyVisible && b.Bounds.Width is > 340 and < 380).ToList();
                    Assert.Equal(4, waiting.Count);
                    Assert.All(waiting, r => Assert.Equal(49, r.Bounds.Height, 0.5));

                    await vm.Glossary.SuggestTermsCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);
                    Capture(window, "glossario-v6-termos-traduzidos", theme);

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

                    AssertTabsRow(window, vm.Glossary, theme);
                    var table = Card(window);
                    Assert.Equal(916, table.Bounds.Width, 1);
                    Assert.Equal(360, panel.Bounds.Width, 1);
                    Assert.Equal(20, panel.TranslatePoint(default, window)!.Value.X - table.TranslatePoint(new Point(table.Bounds.Width, 0), window)!.Value.X, 1);
                    var termRows = Rows(window).Where(r => r.Classes.Contains("term")).ToList();
                    Assert.Equal(4, termRows.Count);
                    Assert.All(termRows, r => Assert.Equal(48, r.Bounds.Height, 0.5));
                    var trigger = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Glossary.SuggestTermsCommand);
                    Assert.True(trigger.Classes.Contains("accent") && trigger.Classes.Contains("sm") && trigger.Bounds.Height == 28);
                    Assert.All(add, b => Assert.Equal(28, b.Bounds.Height));
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
