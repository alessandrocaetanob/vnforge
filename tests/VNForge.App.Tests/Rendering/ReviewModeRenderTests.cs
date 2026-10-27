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
            Assert.Equal(GameTextboxSize.Stage, box.Size);
            var frame = Frame(window, box);
            Assert.True(frame.Left > window.Width / 2 - 40, "a caixa do jogo fica na coluna da direita");
            Assert.True(frame.Right <= window.Width && frame.Bottom <= window.Height, "a caixa cabe na janela");
            Assert.Equal(1d, box.FitScale);

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
            Assert.Equal("Cena 2", review.NextSceneName);
            Assert.Equal("Cena 2", view.FindControl<TextBlock>("NextSceneTitle")!.Text);
            Assert.Equal("1 linha revisada hoje", review.TodayText);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Text?.Contains("yst0", StringComparison.Ordinal) == true);
            Assert.Equal("Cena 1", view.FindControl<TextBlock>("SceneTitle")!.Text);
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.GoToNextSceneCommand);
            Salvar(window, "modo-revisao-fim", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_1366_por_768_a_tela_cobre_o_Editor_e_a_caixa_do_jogo_cabe_na_janela(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var origin = view.TranslatePoint(default, window) ?? throw new InvalidOperationException("a tela não está na janela");
            Assert.True(view.Bounds.Width > 1100 && view.Bounds.Height > 500, $"a tela ocupa o Editor ({view.Bounds.Width}x{view.Bounds.Height})");
            Assert.True(origin.X + view.Bounds.Width <= window.Width && origin.Y + view.Bounds.Height <= window.Height, "a tela cabe na janela");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<PageHeader>(), h => h.IsEffectivelyVisible);

            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>());
            var boxOrigin = box.TranslatePoint(default, window) ?? throw new InvalidOperationException("a caixa não está na janela");
            Assert.True(boxOrigin.X + box.Bounds.Width <= window.Width && boxOrigin.Y + box.Bounds.Height <= window.Height, "a caixa cabe na janela");
            Salvar(window, "modo-revisao-1366", theme);
        }, 1366, 768);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_palco_e_16_por_9_maior_que_o_roteiro_e_guarda_a_caixa_na_metade_de_baixo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            var script = view.FindControl<ScrollViewer>("Script")!;
            var stageFrame = Frame(window, stage);
            Assert.InRange(stageFrame.Width / stageFrame.Height, (16d / 9) - 0.02, (16d / 9) + 0.02);
            Assert.True(stageFrame.Width > script.Bounds.Width, $"o palco ({stageFrame.Width}) é mais largo que o roteiro ({script.Bounds.Width})");
            Assert.True(stageFrame.Right <= window.Width && stageFrame.Bottom <= window.Height);
            Assert.Equal("Cena 1", stage.SceneName);

            var box = Assert.Single(stage.GetVisualDescendants().OfType<GameTextbox>());
            var boxFrame = Frame(window, box);
            Assert.True(stageFrame.Contains(boxFrame.TopLeft) && stageFrame.Contains(boxFrame.BottomRight), "a caixa do jogo está dentro do palco");
            Assert.True(boxFrame.Top > stageFrame.Top + (stageFrame.Height / 2) - 60 && boxFrame.Bottom > stageFrame.Bottom - 40, "a caixa está encostada na base do palco");
            Assert.True(boxFrame.Width > stageFrame.Width * 0.6, $"a caixa ocupa a largura útil do palco ({boxFrame.Width} de {stageFrame.Width})");
            Salvar(window, "modo-revisao-palco", theme);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_fundo_do_palco_e_escuro_nos_dois_temas(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            var probe = PixelProbe.Capture(window, stage);
            var top = Token(stage, "Vnf.StageTop");
            var bottom = Token(stage, "Vnf.StageBottom");

            // Faixa do alto, à direita do cartão de capítulo e longe da caixa: só o degradê (a vinheta começa depois dos 55% do raio).
            var area = new Rect(probe.Width * 0.45, probe.Height * 0.08, probe.Width * 0.3, probe.Height * 0.12);
            var expected = PixelProbe.Blend(bottom, 0.14, top);
            Assert.True(probe.CountInArea(area, expected, 14) > area.Width * area.Height * 0.9, "o alto do palco é o degradê escuro do tema");
            Assert.True(((0.2126 * expected.R) + (0.7152 * expected.G) + (0.0722 * expected.B)) / 255 < 0.3, "o palco é escuro mesmo no tema claro");
        });
    }

    [Theory]
    [InlineData(1440, 1000)]
    [InlineData(1366, 768)]
    public async Task A_barra_de_acoes_fica_inteira_na_janela_e_Esta_boa_aparece_sem_rolar(int width, int height)
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var bar = view.FindControl<Border>("ActionBar")!;
            var barFrame = Frame(window, bar);
            Assert.True(barFrame.Left >= 0 && barFrame.Right <= window.Width && barFrame.Bottom <= window.Height, $"a barra cabe na janela ({barFrame})");
            Assert.True(barFrame.Width > window.Width - 160, "a barra vai de ponta a ponta do Editor");
            Assert.DoesNotContain(bar.GetVisualAncestors(), a => a is ScrollViewer);

            var review = vm.Editor.Review!;
            var approve = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.ApproveCommand);
            var later = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == review.LaterCommand);
            var approveFrame = Frame(window, approve);
            Assert.True(barFrame.Contains(approveFrame.TopLeft) && barFrame.Contains(approveFrame.BottomRight), "Está boa fica dentro da barra");
            Assert.True(approveFrame.Bottom <= window.Height - 20, "Está boa está à vista");
            Assert.DoesNotContain(approve.GetVisualAncestors(), a => a is ScrollViewer);
            Assert.True(approveFrame.Right < Frame(window, later).Left, "Depois e Pular ficam à direita");

            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            Assert.True(Frame(window, stage).Bottom <= barFrame.Top, "o palco não passa para baixo da barra");
        }, width, height);
    }

    [Fact]
    public async Task A_1366_com_aviso_o_palco_encolhe_e_a_barra_continua_inteira()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            vm.Editor.Review!.SkipCommand.Execute(null);
            ScreenRenderer.Settle(window);
            var view = Assert.Single(window.GetVisualDescendants().OfType<ReviewModeView>(), v => v.IsEffectivelyVisible);
            var stageFrame = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            var barFrame = Frame(window, view.FindControl<Border>("ActionBar")!);
            Assert.True(barFrame.Bottom <= window.Height);
            Assert.True(stageFrame.Bottom <= barFrame.Top);
            var warning = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<FitWarning>(), w => w.IsEffectivelyVisible));
            Assert.True(warning.Top >= stageFrame.Bottom && warning.Bottom <= barFrame.Top, "o aviso cabe entre o palco e a barra");
            Salvar(window, "modo-revisao-passa-1366", ThemeVariant.Dark);
        }, 1366, 768);
    }

    private async Task Abrir(string root, ThemeVariant theme, Action<MainWindow, MainWindowViewModel> verificar, int width = ScreenRenderer.Width, int height = ScreenRenderer.Height)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                using var motion = MotionPreferences.Force(false);
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
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

    /// <summary>Onde o controle está na janela, em pixels dela, já com qualquer escala que um Viewbox aplique.</summary>
    private static Rect Frame(Visual root, Control control)
    {
        var origin = control.TranslatePoint(default, root) ?? throw new InvalidOperationException("O controle não está na janela.");
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), root) ?? throw new InvalidOperationException("O controle não está na janela.");
        return new Rect(origin, end);
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
