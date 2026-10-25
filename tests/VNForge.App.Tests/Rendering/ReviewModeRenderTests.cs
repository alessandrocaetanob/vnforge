using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O modo revisão montado de verdade a 1440×1000 nos dois temas: uma fala que cabe, uma que passa da caixa de um personagem sem gênero e a tela final.
/// Os PNG (<c>VNFORGE_RENDER_DIR</c>) vão ao lado do artboard no PR. A conferência é pela tinta em retângulos conhecidos da caixa do jogo e pela cor
/// de fundo do aviso, não por faixas de pixels de texto, porque a fonte muda de um sistema para outro.
/// </summary>
public sealed class ReviewModeRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Tinta = 24;
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly string LongText = "O nosso professor responsável já estava de pé, esperando por nós dois de braços cruzados na frente do portão da escola, debaixo da chuva.";

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Fala_que_cabe_mostra_a_caixa_do_jogo_com_tinta_e_as_duas_colunas(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            Assert.Equal("Linha 1 de 3", vm.Editor.Review!.PositionText);
            Assert.True(view.Bounds.Width > 1200 && view.Bounds.Height > 600, "a tela ocupa o Editor");

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            Assert.Equal(GameTextboxSize.Large, box.Size);
            var origin = box.TranslatePoint(default, window) ?? throw new InvalidOperationException("a caixa não está na janela");
            Assert.True(origin.X > window.Width / 2 - 40, "a caixa do jogo fica na coluna da direita");
            Assert.True(origin.X + box.Bounds.Width <= window.Width && origin.Y + box.Bounds.Height <= window.Height, "a caixa cabe na janela");

            var probe = PixelProbe.Capture(window, box);
            var ink = Token(box, "Vnf.TextboxInk");
            var rows = box.RowBounds();
            Assert.Equal([GameTextboxRowKind.Text, GameTextboxRowKind.Vacant, GameTextboxRowKind.Vacant], rows.Select(r => r.Row.Kind));
            Assert.True(probe.CountInArea(rows[0].Bounds, ink, Tinta) >= 3, "a primeira linha tem tinta");
            Assert.Equal(0, probe.CountInArea(rows[1].Bounds, ink, Tinta));

            Assert.Equal("Cabe · 1 de 3 linhas", vm.Editor.Review.Detail!.FitText);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
            Salvar(window, "modo-revisao-cabe", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Fala_que_passa_mostra_o_traco_o_aviso_com_as_saidas_e_o_aviso_de_genero(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            var probe = PixelProbe.Capture(window, box);
            var overflow = Token(box, "Vnf.TextboxOverflow");
            var over = Assert.Single(box.RowBounds(), r => r.Row.Kind == GameTextboxRowKind.Overflow && r.Row.FirstOverflow);
            Assert.True(probe.CountInArea(over.Bounds, overflow, Tinta) >= 3, "a linha que passa vai na cor de overflow");

            var warning = Assert.Single(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible);
            Assert.NotNull(warning.SplitCommand);
            Assert.NotNull(warning.ShortenCommand);
            var warningProbe = PixelProbe.Capture(window, warning);
            var expected = theme == ThemeVariant.Light ? Color.Parse("#FFF1D6") : Color.Parse("#3A2C10");
            Assert.True(warningProbe.Near(10, 6, expected, 6), $"o fundo do aviso é o do tema ({warningProbe.At(10, 6)} em vez de {expected})");

            Assert.True(vm.Editor.Review!.GenderMissing);
            var gender = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Review.DefineGenderCommand);
            Assert.True(gender.Bounds.Width > 40 && gender.Bounds.Height > 16);
            Salvar(window, "modo-revisao-passa", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Tela_final_mostra_o_resumo_e_a_proxima_cena(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var review = vm.Editor.Review!;
            review.ApproveCommand.Execute(null);
            review.LaterCommand.Execute(null);
            review.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);

            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var card = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Width == 520);
            var origin = card.TranslatePoint(default, window) ?? throw new InvalidOperationException("o cartão não está na janela");
            Assert.True(origin.X > 0 && origin.X + card.Bounds.Width < window.Width && origin.Y > 0 && origin.Y + card.Bounds.Height < window.Height);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<GameTextbox>(), b => b.IsEffectivelyVisible);
            Assert.Equal("1 linha aprovada, 1 deixada para depois e 1 pulada.", review.FinishedText);
            Assert.Equal("yst00002", review.NextSceneName);
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.GoToNextSceneCommand);
            Salvar(window, "modo-revisao-fim", theme);
        });
    }

    private async Task Abrir(string root, ThemeVariant theme, Action<MainWindow, MainWindowViewModel> verificar)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    ScreenRenderer.Settle(window);

                    var entry = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.StartReviewCommand);
                    entry.Command!.Execute(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(vm.Editor.IsReviewing);

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

    private static Color Token(Control host, string key)
    {
        Assert.True(host.TryFindResource(key, host.ActualThemeVariant, out var found), key);
        return Assert.IsType<ISolidColorBrush>(found, exactMatch: false).Color;
    }

    private static void Salvar(Window window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(name, theme)));
    }

    private static string CreateProject(string parent)
    {
        var folder = ProjectFolder.Create(parent, new ProjectConfig(
            DemoProject.Name,
            new GameConfig(@"D:\Jogos\MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        folder.SaveFormatting(FormattingProfile.Default with { Wrap = new WrapRule(Enabled: true, MaxWidth: 40, MaxLines: 3, CharWidth: CharWidthMode.Uniform) });
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta")]);
        folder.SaveText(
            "yst00001",
            [
                Line("yst00001", 1, "美咲", @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda!\p"),
                Line("yst00001", 2, "健太", @"担任の先生が腕を組んで立っていた。\p", LongText + @"\p"),
                Line("yst00001", 3, "美咲", @"「うん」\p", @"Aham.\p"),
            ]);
        folder.SaveText("yst00002", [Line("yst00002", 1, "美咲", @"「そう」\p", @"Isso.\p")]);
        return folder.Root;
    }

    private static TextUnit Line(string script, int number, string speaker, string source, string translation) =>
        new($"{script}:{number:D6}", TextUnitKind.Dialogue, speaker, source, translation, TranslationStatus.Machine);
}
