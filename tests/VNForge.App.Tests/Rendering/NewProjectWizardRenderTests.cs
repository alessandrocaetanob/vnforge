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
using VNForge.Core.Vndb;
using Xunit.Sdk;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Os passos do assistente de novo projeto no design system v3 (quadro da #547), a 1440×1000 nos dois temas, para pôr ao lado do artboard no PR.
/// Além do PNG, confere a moldura do quadro (coluna de 960, passos com círculo de 24, título de 34), as medidas dos cartões, a tinta dentro de retângulos
/// conhecidos (a caixa de cada cartão de acentos) e os textos visíveis de cada passo.
/// Os PNGs saem em <c>VNFORGE_RENDER_DIR</c> como <c>novo-projeto-v6-N-passo-tema.png</c>.
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
                using var temp = new TempFolder($"932-0xAB-{themeName}-");
                var game = temp.SubFolder("jogo");
                File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
                var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new PatchableEngine(312)], appSettings: new AppSettingsFile(temp.SubFolder("config")), clock: new FixedClock(Now), vndb: VndbCatalog());
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, newProjectWizard: () => wizard);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    start.NewProjectCommand.Execute(null);
                    Assert.Same(wizard, vm.CurrentPage);

                    wizard.SetLibraryGame(new LibraryGameContext("Amaoto no Hakoniwa", "v90001", null));
                    await wizard.SetGameFolderAsync(game);
                    Render(window, "novo-projeto-v6-1-jogo", theme);
                    CheckGamePlain(window, temp.Path);
                    var gameTexts = VisibleTexts(window);
                    Assert.Contains("Amaoto no Hakoniwa", gameTexts);
                    Assert.Contains("VNDB v90001", gameTexts);
                    CheckFrame(window, 1);

                    wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.SearchTitle = "Amaoto";
                    await wizard.SearchVndbCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-v6-2-vndb-escolha", theme);
                    CheckVndbChoice(window, temp.Path);
                    CheckFrame(window, 2);

                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-v6-2-vndb-ficha", theme);
                    CheckVndbPreview(window, temp.Path);
                    CheckFrame(window, 2);

                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.ProjectName = "Minha VN";
                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-v6-3-acentos", theme);
                    CheckAccents(window, temp.Path);
                    CheckFrame(window, 4);

                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.RateText = "5,40";
                    wizard.LimitText = "300";
                    Render(window, "novo-projeto-v6-4-ia-e-gasto", theme);
                    CheckAi(window, wizard, temp.Path);
                    CheckFrame(window, 5);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Os_detalhes_tecnicos_abertos_mostram_o_formato_a_chave_a_pagina_de_codigo_e_o_modelo()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var game = temp.SubFolder("jogo");
                File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
                var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new PatchableEngine(312)], appSettings: new AppSettingsFile(temp.SubFolder("config")), clock: new FixedClock(Now));
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, newProjectWizard: () => wizard);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                try
                {
                    window.Show();
                    start.NewProjectCommand.Execute(null);
                    await wizard.SetGameFolderAsync(game);
                    ScreenRenderer.Settle(window);
                    OpenTechnicalDetails(window);
                    var gameTexts = VisibleTexts(window);
                    Assert.Contains(gameTexts, t => t.Contains("YPF", StringComparison.Ordinal));
                    Assert.Contains("Chave XOR dos scripts", gameTexts);
                    Assert.Contains(gameTexts, t => t.StartsWith("932", StringComparison.Ordinal));

                    wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
                    await wizard.NextCommand.ExecuteAsync(null);
                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.ProjectName = "Minha VN";
                    await wizard.NextCommand.ExecuteAsync(null);
                    await wizard.NextCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);
                    OpenTechnicalDetails(window);
                    Assert.Contains(VisibleTexts(window), t => t.Contains("Opus", StringComparison.Ordinal));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void OpenTechnicalDetails(MainWindow window)
    {
        foreach (var details in window.GetVisualDescendants().OfType<TechnicalDetails>().Where(d => d.IsEffectivelyVisible))
        {
            details.IsExpanded = true;
        }

        ScreenRenderer.Settle(window);
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

    private static void AssertPlain(IEnumerable<string> texts, string pathToIgnore) =>
        Assert.All(texts, text => Assert.DoesNotMatch(PlainForbidden, text.Replace(pathToIgnore, string.Empty, StringComparison.OrdinalIgnoreCase)));

    [Theory]
    [InlineData("932 (Shift-JIS)")]
    [InlineData("Página de código 932")]
    [InlineData("Chave 0xD36FAC96")]
    public void A_deteccao_de_jargao_continua_pegando_o_termo_fora_do_caminho_da_pasta(string text)
    {
        const string folder = "/tmp/vnforge-app-tests/932-0xAB/jogo";

        AssertPlain([folder], folder);
        AssertPlain([$"{folder} é a pasta do jogo"], folder);
        Assert.Throws<AllException>(() => AssertPlain([text, folder], folder));
        Assert.Throws<AllException>(() => AssertPlain([$"{folder} · {text}"], folder));
    }

    private static VNForge.Vndb.FakeVndbCatalog VndbCatalog()
    {
        var main = new VndbVisualNovel(
            "v31201", "Amaoto no Hakoniwa", "雨音の箱庭", [], ["Amaoto jogo"], "ja", 0, "2019-05-31", ["ja"], ["win"], null, 3, 1080, 10,
            "Numa cidade onde chove quase o ano inteiro, Kenta volta à escola em que o irmão mais velho estudou e encontra uma caixa de música que só toca quando alguém mente.", []);
        var disc = main with { Id = "v31202", Title = "Amaoto no Hakoniwa: Fan Disc", AltTitle = "雨音の箱庭 ファンディスク", Aliases = [], Released = "2020-03-01", Length = 2, LengthMinutes = 180 };
        VndbCharacter Character(string id, string name, string original, string? gender, string role, params string[] aliases) =>
            new(id, name, original, aliases, null, null, gender is null ? null : new VndbGender(gender, gender), [new VndbCharacterAppearance("v31201", role, 0, null)], []);
        return new VNForge.Vndb.FakeVndbCatalog(
            [main, disc],
            [
                Character("c1", "Kenta Aizawa", "相沢 健太", "m", "main", "Ken", "Kenta-kun"),
                Character("c2", "Aoi Sakura", "佐倉 葵", "f", "primary", "Aoi-chan"),
                Character("c3", "Ren Hayase", "早瀬 蓮", "m", "side", "Ren"),
                Character("c4", "Misaki Todo", "藤堂 美咲", "f", "side", "Todo-sensei"),
                Character("c5", "Coadjuvante", "名無し", null, "appears"),
            ]);
    }

    private static void CheckVndbChoice(Window window, string tempRoot)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Achamos 2 jogos com este título no VNDB", texts);
        Assert.Contains("Combina com a pasta e o exe", texts);
        Assert.Contains("Só o título", texts);
        Assert.Contains("Nenhum deles, seguir sem VNDB", texts);
        Assert.Contains("Buscar por outro título", texts);
        Assert.Contains("Passo 2 de 5", texts);
        Assert.DoesNotContain(VndbCredit.ShortText, texts);
        AssertPlain(texts, tempRoot);
    }

    private static void CheckVndbPreview(Window window, string tempRoot)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Personagens que chegam", texts);
        Assert.Contains("Sinopse para a bíblia", texts);
        Assert.Contains("Aviso que some", texts);
        Assert.DoesNotContain(VndbCredit.ShortText, texts);
        Assert.Contains("Amaoto no Hakoniwa, v31201", texts);
        Assert.Contains("Kenta Aizawa", texts);
        Assert.Contains("相沢 健太", texts);
        Assert.Contains("Masculino", texts);
        Assert.Contains("Sem gênero", texts);
        AssertPlain(texts, tempRoot);
    }

    private static void CheckGamePlain(Window window, string tempRoot)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Feito com o motor Yu-RIS", texts);
        Assert.Contains("312 arquivos de roteiro", texts);
        Assert.Contains("Jogo reconhecido", texts);
        Assert.Contains("Escolher outra pasta", texts);
        AssertPlain(texts, tempRoot);

        Assert.Equal(FontWeight.SemiBold, TextOf(window, "Pasta do jogo").FontWeight);
        Assert.Equal(FontWeight.Medium, TextOf(window, "312 arquivos de roteiro").FontWeight);
        var pill = TextOf(window, "Suportado");
        Assert.Equal((11d, FontWeight.SemiBold), (pill.FontSize, pill.FontWeight));

        var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panel") && b.IsEffectivelyVisible).ToArray();
        Assert.Equal(2, cards.Length);
        Assert.All(cards, card => Assert.Equal(16, card.CornerRadius.TopLeft));
        Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 959, 961));

        var back = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content is "Voltar");
        var next = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content is "Avançar");
        Assert.Equal(32, back.Bounds.Height);
        Assert.InRange(next.Bounds.Width, 88, 91);
        Assert.Equal(FontWeight.SemiBold, next.FontWeight);
    }

    /// <summary>A moldura que todos os passos repetem: a coluna de 960, o título de 34, o Cancelar e o passo a passo com os círculos numerados do quadro.</summary>
    private static void CheckFrame(Window window, int step)
    {
        var title = TextOf(window, "Novo projeto");
        Assert.Equal(34, title.FontSize);
        Assert.Equal($"Passo {step} de 5", Assert.Single(VisibleTexts(window), t => t.StartsWith("Passo ", StringComparison.Ordinal)));
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content is "Cancelar");

        var stepper = window.GetVisualDescendants().OfType<PipelineStepper>().Single(s => s.IsEffectivelyVisible);
        Assert.InRange(stepper.Bounds.Width, 959, 961);
        Assert.InRange(stepper.Bounds.Height, 62, 64);
        Assert.InRange(stepper.TranslatePoint(default, window)!.Value.X, 279, 281);

        var circles = stepper.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_Circle").ToArray();
        Assert.Equal(5, circles.Length);
        Assert.All(circles, c => Assert.Equal((24d, 24d), (c.Bounds.Width, c.Bounds.Height)));
        var numbers = stepper.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_Number" && t.IsEffectivelyVisible).Select(t => t.Text);
        Assert.Equal(Enumerable.Range(step, 6 - step).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), numbers);
    }

    private static TextBlock TextOf(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == text);

    private static void CheckAccents(Window window, string tempRoot)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Sem acentos", texts);
        Assert.Contains("Com acentos, fonte modificada", texts);
        Assert.Contains("Com acentos, ajuste no jogo", texts);
        Assert.Contains("Em breve", texts);
        Assert.Contains("Recomendada para este jogo", texts);
        AssertPlain(texts, tempRoot);

        var cards = window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.GroupName == "WizardAccents").ToArray();
        Assert.Equal([true, false, true], cards.Select(c => c.IsEnabled));

        var boxes = window.GetVisualDescendants().OfType<GameTextbox>().ToArray();
        Assert.Equal(3, boxes.Length);
        Assert.All(cards, card => Assert.InRange(card.Bounds.Height, 263, 265));
        Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 311, 313));
        foreach (var box in boxes)
        {
            Assert.InRange(box.Bounds.Width, 277, 279);
            Assert.InRange(box.Bounds.Height, 89, 91);
            var ink = Token(box, "Vnf.TextboxInk");
            var probe = PixelProbe.Capture(window, box);
            var lines = box.RowBounds().Where(r => r.Row.Kind == GameTextboxRowKind.Text).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.True(probe.CountInArea(line.Bounds, ink, Ink) >= MinimumInk, "a linha da frase de teste tem tinta"));
        }
    }

    private static void CheckAi(Window window, NewProjectWizardViewModel wizard, string tempRoot)
    {
        var texts = VisibleTexts(window);
        Assert.Contains("Rápida", texts);
        Assert.Contains("Equilibrada", texts);
        Assert.Contains("Caprichada", texts);
        AssertPlain(texts, tempRoot);

        var cards = window.GetVisualDescendants().OfType<QualityCard>().ToArray();
        Assert.Equal(["Rápida", "Equilibrada", "Caprichada"], cards.Select(c => c.Title));
        Assert.All(cards, card => Assert.StartsWith("R$", card.PriceText));
        Assert.All(cards, card => Assert.False(card.ExampleRate));
        Assert.Equal(wizard.Qualities.Select(q => q.Price), cards.Select(c => c.Price));
        Assert.All(cards, card => Assert.InRange(card.Bounds.Height, 125, 127));
        Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 311, 313));

        var budget = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("panel") && b.IsEffectivelyVisible);
        Assert.InRange(budget.Bounds.Height, 125, 127);
        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("banner") && b.IsEffectivelyVisible);
        Assert.Equal((16, 66), (banner.CornerRadius.TopLeft, Math.Round(banner.Bounds.Height)));

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
