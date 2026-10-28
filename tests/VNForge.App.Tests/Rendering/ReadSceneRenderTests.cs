using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// O leitor de cena (#475) montado de verdade nos dois temas e guiado só pelo teclado: a cena em tela cheia, uma fala por vez na caixa do jogo, Espaço para avançar,
/// seta para voltar, F para marcar e Esc para sair com o filtro das falas marcadas. A conferência é pela tinta em retângulos conhecidos e pelas posições, não por
/// faixas de pixels de texto, porque a fonte muda de um sistema para outro.
/// </summary>
public sealed class ReadSceneRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int Tinta = 24;
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_cena_ocupa_o_Editor_com_a_caixa_do_jogo_e_so_as_teclas_da_leitura(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var view = Visivel(window);
            Assert.True(view.Bounds.Width > 1200 && view.Bounds.Height > 600, "a leitura ocupa o Editor");
            Assert.Equal("Fala 1 de 4", vm.Editor.Reading!.PositionText);
            Assert.True(view.FindControl<Grid>("Root")!.IsFocused, "a tela recebe o foco ao abrir");

            var stage = Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>());
            var stageFrame = Frame(window, stage);
            Assert.InRange(stageFrame.Width / stageFrame.Height, (16d / 9) - 0.02, (16d / 9) + 0.02);
            Assert.True(stageFrame.Width > window.Width * 0.55, $"o palco é a maior parte da tela ({stageFrame.Width} de {window.Width})");
            Assert.True(stageFrame.Right <= window.Width && stageFrame.Bottom <= window.Height);

            var box = Assert.Single(stage.GetVisualDescendants().OfType<GameTextbox>());
            Assert.Equal(GameTextboxSize.Stage, box.Size);
            var boxFrame = Frame(window, box);
            Assert.True(stageFrame.Contains(boxFrame.TopLeft) && stageFrame.Contains(boxFrame.BottomRight), "a caixa do jogo está dentro do palco");
            var probe = PixelProbe.Capture(window, box);
            var ink = Token(box, "Vnf.TextboxInk");
            var rows = box.RowBounds();
            Assert.True(probe.CountInArea(rows[0].Bounds, ink, Tinta) >= 3, "a primeira linha tem tinta");

            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Está boa");
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Reading.NextCommand);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), b => b.IsEffectivelyVisible && AutomationProperties(b) == "Fala marcada");
            Salvar(window, "ler-como-jogador", theme);
        });
    }

    [Fact]
    public async Task Espaco_avanca_a_seta_volta_e_segurar_a_tecla_anda_uma_fala_so()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            var leitor = vm.Editor.Reading!;

            Teclar(window, Key.Space, PhysicalKey.Space);
            Assert.Equal("Fala 2 de 4", leitor.PositionText);

            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            ScreenRenderer.Settle(window);
            Assert.Equal("Fala 3 de 4", leitor.PositionText);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);

            Teclar(window, Key.Left, PhysicalKey.ArrowLeft);
            Teclar(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal("Fala 1 de 4", leitor.PositionText);
            Teclar(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.Equal("Fala 1 de 4", leitor.PositionText);

            Assert.Equal(
                [TranslationStatus.Machine, TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated],
                ProjectFolder.Open(root).LoadText("yst00001").Select(u => u.Status));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task F_marca_a_fala_com_o_selo_e_o_botao_aceso_e_Esc_abre_o_Editor_com_o_filtro(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            var leitor = vm.Editor.Reading!;
            Teclar(window, Key.Space, PhysicalKey.Space);
            Teclar(window, Key.F, PhysicalKey.F);

            var view = Visivel(window);
            Assert.True(leitor.IsOdd);
            Assert.True(view.FindControl<StackPanel>("OddBadge")!.IsEffectivelyVisible, "o selo da fala marcada aparece");
            Assert.True(view.FindControl<Button>("OddButton")!.Classes.Contains("accent"), "o botão fica aceso");
            Assert.Equal("1 fala soa estranho", Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == leitor.OddText).Text);
            Assert.Equal([ReadingFlags.SoundsOdd], ProjectFolder.Open(root).LoadText("yst00001")[1].Flags);
            Salvar(window, "ler-como-jogador-marcada", theme);

            Teclar(window, Key.Escape, PhysicalKey.Escape);

            Assert.False(vm.Editor.IsReadingScene);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ReadSceneView>(), v => v.IsEffectivelyVisible);
            Assert.Equal(LineStatusFilter.Odd, vm.Editor.Filters.Single(f => f.IsSelected).Filter);
            Assert.Equal(["yst00001:000001"], vm.Editor.Lines.Select(l => l.Id));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_fim_da_cena_mostra_o_cartao_centrado_e_voltar_leva_a_ultima_fala(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, theme, (window, vm) =>
        {
            for (var n = 0; n < 4; n++)
            {
                Teclar(window, Key.Space, PhysicalKey.Space);
            }

            var view = Visivel(window);
            var leitor = vm.Editor.Reading!;
            Assert.True(leitor.IsFinished);
            var card = view.FindControl<Border>("FinishCard")!;
            Assert.True(card.IsEffectivelyVisible);
            var origin = card.TranslatePoint(default, window) ?? throw new InvalidOperationException("o cartão não está na janela");
            Assert.True(origin.X > 0 && origin.X + card.Bounds.Width < window.Width && origin.Y > 0 && origin.Y + card.Bounds.Height < window.Height);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<GameTextbox>(), b => b.IsEffectivelyVisible);
            Assert.Equal("Nenhuma fala marcada como estranha.", view.FindControl<TextBlock>("FinishedText")!.Text);
            Salvar(window, "ler-como-jogador-fim", theme);

            Teclar(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.False(leitor.IsFinished);
            Assert.Equal("Fala 4 de 4", leitor.PositionText);
        });
    }

    [Theory]
    [InlineData(1440, 1000)]
    [InlineData(1366, 768)]
    public async Task A_barra_de_teclas_fica_inteira_na_janela_e_o_palco_nao_passa_para_baixo_dela(int width, int height)
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            Teclar(window, Key.Space, PhysicalKey.Space);
            var view = Visivel(window);
            var bar = Frame(window, view.FindControl<Border>("HintBar")!);
            var stage = Frame(window, Assert.Single(view.GetVisualDescendants().OfType<ReviewStage>()));
            Assert.True(bar.Left >= 0 && bar.Right <= window.Width && bar.Bottom <= window.Height, $"a barra cabe na janela ({bar})");
            Assert.True(stage.Bottom <= bar.Top, "o palco não passa para baixo da barra");
            Assert.True(stage.Top >= Frame(window, view.FindControl<Grid>("TopBar")!).Bottom - 1, "o palco começa depois do topo");
            var odd = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.Reading!.ToggleOddCommand);
            Assert.True(bar.Contains(Frame(window, odd).TopLeft) && bar.Contains(Frame(window, odd).BottomRight), "Soa estranho fica dentro da barra");
            Salvar(window, $"ler-como-jogador-{width}", ThemeVariant.Dark);
        }, width, height);
    }

    [Fact]
    public async Task Fala_sem_traducao_mostra_o_original_na_caixa_e_avisa()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Abrir(root, ThemeVariant.Dark, (window, vm) =>
        {
            for (var n = 0; n < 3; n++)
            {
                Teclar(window, Key.Space, PhysicalKey.Space);
            }

            var view = Visivel(window);
            Assert.True(vm.Editor.Reading!.ShowsSource);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Sem tradução ainda", StringComparison.Ordinal) == true);
            var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>(), b => b.IsEffectivelyVisible);
            var probe = PixelProbe.Capture(window, box);
            Assert.True(probe.CountInArea(box.RowBounds()[0].Bounds, Token(box, "Vnf.TextboxInk"), Tinta) >= 3, "o original aparece na caixa");
        });
    }

    private static string? AutomationProperties(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    private static ReadSceneView Visivel(Visual window) => Assert.Single(window.GetVisualDescendants().OfType<ReadSceneView>(), v => v.IsEffectivelyVisible);

    private static void Teclar(MainWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        ScreenRenderer.Settle(window);
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

                    var entry = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Command == vm.Editor.StartReadingCommand);
                    entry.Command!.Execute(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(vm.Editor.IsReadingScene);

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
                Line(0, "美咲", @"「平気平気！　ほら、行こう！」\p", @"Tranquilo! Vamos, anda!\p", TranslationStatus.Machine),
                Line(1, "健太", @"担任の先生が腕を組んで立っていた。\p", @"O professor já estava de pé, esperando por nós de braços cruzados.\p", TranslationStatus.Approved),
                Line(2, "美咲", @"「うん」\p", @"Aham.\p", TranslationStatus.Machine),
                Line(3, null, @"雨が降っている。\p", null, TranslationStatus.Untranslated),
            ]);
        folder.SaveText("yst00002", [new TextUnit("yst00002:000000", TextUnitKind.Dialogue, "美咲", @"「そう」\p", @"Isso.\p", TranslationStatus.Machine)]);
        return folder.Root;
    }

    private static TextUnit Line(int n, string? speaker, string source, string? translation, TranslationStatus status) =>
        new($"yst00001:{n:D6}", speaker is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, speaker, source, translation, status);
}
