using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using Shapes = Avalonia.Controls.Shapes;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// "Testar no jogo" dentro do shell, em 1440×1000 e nos dois temas, nos três estados do quadro e4c (<c>testar-no-jogo-v6-travado</c>, <c>-traduzido</c> e
/// <c>-falhou</c>) e com um projeto que nunca testou (<c>-sem-teste</c>, os cinco passos cinza). A medida confere as caixas do quadro (largura do cartão, margem,
/// altura dos passos, tamanho do interruptor) e a conferência de cor olha a tinta em retângulos conhecidos (o ponto de estado, o ícone do primeiro passo),
/// não faixas de texto, porque a fonte da CI em Linux não é a do Windows. Os PNGs saem em <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class TestInGameRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Translated = "traduzido";
    private const string Blocked = "travado";
    private const string NeverTested = "sem-teste";
    private const string Failed = "falhou";

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string> Cases => new()
    {
        { "Dark", Blocked },
        { "Light", Blocked },
        { "Dark", Translated },
        { "Light", Translated },
        { "Dark", Failed },
        { "Light", Failed },
        { "Dark", NeverTested },
        { "Light", NeverTested },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Renderiza_a_tela_testar_no_jogo(string themeName, string mode) => RenderAsync(themeName, mode, ScreenRenderer.Width, ScreenRenderer.Height, "testar-no-jogo-v6-" + mode);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task Com_erros_tudo_cabe_na_janela_de_1366_por_768(string themeName) => RenderAsync(themeName, Blocked, 1366, 768, "testar-no-jogo-v6-travado-1366");

    private async Task RenderAsync(string themeName, string mode, int width, int height, string fileName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"), injected: mode is Translated or Failed);
        if (mode == Translated)
        {
            var steps = new[] { GameTestStep.Check, GameTestStep.Build, GameTestStep.Backup, GameTestStep.Inject, GameTestStep.Open };
            var times = new long[] { 400, 2_100, 300, 600, 100 };
            GameStateFile.Save(ProjectFolder.Open(root), new GameStateFile(
                GameStateFile.CurrentVersion,
                new GameInGame(GameTestKind.Translation, new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316", null, null),
                new GameLastTest(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), GameTestKind.Translation, [.. steps.Select((s, i) => new GameTestStepTime(s, times[i]))])));
        }

        await fixture.Session.Dispatch(
            async () =>
            {
                using var config = new TempFolder();
                var tasks = new BackgroundTaskService(a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
                var qa = new DemoQaRule { Enabled = mode == Blocked };
                var build = new BuildViewModel(tasks, [new DemoBuildEngine()], mode == Failed ? new RefusingStarter() : new NoLaunchStarter(), new QaEngine([qa]), new FixedClock(Now));
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, taskService: tasks, build: build);
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Build);
                    build.SetOpenScene("yst00013", "Capítulo 1 · Manhã de chuva");
                    await build.ReloadAsync();
                    if (mode == Failed)
                    {
                        await build.TestCommand.ExecuteAsync(null);

                        // O andamento volta à janela pelo Dispatcher, então o último passo chega depois do fim do comando.
                        for (var i = 0; i < 100 && build.LastTestSteps.Count == 0 || build.LastTestSteps[^1].State != TestStepState.Failed; i++)
                        {
                            await Task.Delay(50, TestContext.Current.CancellationToken);
                            ScreenRenderer.Settle(window);
                        }
                    }

                    ScreenRenderer.Settle(window);
                    var path = RenderOutput.PathFor(ScreenRenderer.FileName(fileName, theme));
                    ScreenRenderer.Save(window, path);
                    using (var png = new Bitmap(path))
                    {
                        Assert.Equal(width, png.PixelSize.Width);
                        Assert.Equal(height, png.PixelSize.Height);
                    }

                    var view = window.GetVisualDescendants().OfType<BuildView>().Single();
                    Inspect(window, view, build, theme, mode, width);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(Window window, BuildView view, BuildViewModel vm, ThemeVariant theme, string mode, int width)
    {
        var banner = view.GetVisualDescendants().OfType<GameStateBanner>().First();
        Assert.Contains("stage", banner.Classes);
        Assert.Equal(mode == Blocked ? GameState.Blocked : mode == NeverTested ? GameState.Original : GameState.Translated, banner.State);

        // As caixas do quadro: o cartão começa a 32 px do trilho e termina a 32 px da borda, com 20 px de respiro sob a barra de abas.
        var origin = banner.TranslatePoint(default, window)!.Value;
        var column = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(width - 80 - 64, banner.Bounds.Width, 1);
        Assert.Equal(column.TranslatePoint(default, window)!.Value.X + 32, origin.X, 1);
        Assert.Equal(column.TranslatePoint(default, window)!.Value.Y + 20, origin.Y, 1);
        if (width == ScreenRenderer.Width)
        {
            Assert.InRange(banner.Bounds.Height, 184, 190);
        }

        var dot = banner.GetVisualDescendants().OfType<Shapes.Ellipse>().Single(e => e.Name == "PART_Dot");
        Assert.Equal(10, dot.Bounds.Width);
        var fill = ((ISolidColorBrush)dot.Fill!).Color;
        var dotOrigin = dot.TranslatePoint(default, banner) ?? throw new InvalidOperationException("O ponto não está na faixa.");
        var probe = PixelProbe.Capture(window, banner);
        Assert.True(probe.CountInArea(new Rect(dotOrigin, dot.Bounds.Size), fill, 24) > 40, "O ponto de estado não tem a cor do estado.");

        var back = Named<Button>(view, "Voltar ao jogo original");
        Assert.True(back.IsEffectivelyVisible);
        Assert.Equal(mode is Translated or Failed, back.IsEffectivelyEnabled);
        Assert.True(back.Classes.Contains("danger"), "Voltar ao jogo original é o botão de perigo.");
        Assert.DoesNotContain("accent", back.Classes);
        Assert.Equal(32, back.Bounds.Height);
        InsideBanner(banner, back);

        var test = Named<Button>(view, "Testar no jogo");
        Assert.Equal(mode != Blocked, test.IsEffectivelyEnabled);
        Assert.True(test.Classes.Contains("lg"), "O botão principal é o grande.");
        Assert.Equal(40, test.Bounds.Height);
        InsideBanner(banner, test);
        var corner = test.TranslatePoint(new Point(test.Bounds.Width - 1, test.Bounds.Height - 1), window)!.Value;
        Assert.True(new Rect(window.Bounds.Size).Contains(corner), "O botão principal sai da janela.");

        // Uma tela, um botão de destaque: Ver os erros quando trava, Testar no jogo quando pode.
        var showErrors = Named<Button>(view, "Ver os erros");
        Assert.Equal(mode == Blocked, showErrors.IsEffectivelyVisible);
        Assert.Equal(mode == Blocked ? 32 : 0, showErrors.Bounds.Height);
        var accents = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("accent")).ToList();
        Assert.Equal(mode == Blocked ? [showErrors] : [test], accents);
        Assert.True(Named<Button>(view, "Testar só os acentos").IsEffectivelyEnabled);

        var steps = Named<ItemsControl>(view, "Passos do último teste");
        Assert.True(steps.IsEffectivelyVisible);
        InsideBanner(banner, steps);
        Assert.Equal(["Conferir a tradução", "Preparar os arquivos", "Guardar a cópia de segurança", "Copiar para o jogo", "Abrir o jogo"], vm.LastTestSteps.Select(s => s.Label));
        var cells = steps.GetVisualDescendants().OfType<Grid>().Where(g => g.DataContext is TestStepRow).ToList();
        Assert.Equal(5, cells.Count);
        var rects = cells.Select(c => new Rect(c.TranslatePoint(default, steps)!.Value, c.Bounds.Size)).ToList();
        Assert.All(rects, r => Assert.Equal(rects[0].Y, r.Y));
        Assert.All(rects, r => Assert.InRange(r.Width, 100, width - 80));
        Assert.All(cells, c => Assert.Equal(new Size(20, 20), c.GetVisualDescendants().OfType<Panel>().First().Bounds.Size));
        for (var i = 1; i < rects.Count; i++)
        {
            Assert.True(rects[i - 1].Right <= rects[i].X + 1, "Passos sobrepostos.");
        }

        var texts = steps.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        var caption = Named<TextBlock>(view, "Resumo dos passos").Text;
        var band = view.GetVisualDescendants().OfType<GameStateBanner>().Single(b => b.GetValue(AutomationProperties.NameProperty) == "Mensagem de erro");
        switch (mode)
        {
            case Blocked:
                Assert.True(vm.LastTestSteps[0].IsBlocked);
                Assert.All(vm.LastTestSteps.Skip(1), r => Assert.True(r.IsPending));
                Assert.Contains("2 erros travam", texts);
                Assert.Equal("2 erros travam o teste no jogo", banner.Title);
                Assert.Equal("Falta ou sobra um marcador (2). Corrija em Conferir. O jogo está como veio.", banner.Message);
                Assert.Equal("Corrija os erros em Conferir para testar de novo.", caption);
                AssertIcon(window, steps, theme, "Vnf.TextSecondary", cells[0]);
                break;
            case Failed:
                Assert.Equal(
                    [TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Failed],
                    vm.LastTestSteps.Select(r => r.State));
                Assert.Contains("O jogo não abriu", texts);
                Assert.Equal("O último teste falhou.", caption);
                Assert.Equal("O jogo está com a tradução de 08/11", banner.Title);
                AssertIcon(window, steps, theme, "Vnf.TextSecondary", cells[4]);
                break;
            case NeverTested:
                Assert.All(vm.LastTestSteps, r => Assert.True(r.IsPending));
                Assert.Equal("Ainda não houve teste neste projeto", caption);
                break;
            default:
                Assert.All(vm.LastTestSteps, r => Assert.True(r.IsDone));
                Assert.Equal(["0,4 s", "2,1 s", "0,3 s", "0,6 s", "0,1 s"], vm.LastTestSteps.Select(r => r.TimeText));
                Assert.Contains("0,4 s", texts);
                Assert.StartsWith("Último teste · ", caption, StringComparison.Ordinal);
                Assert.EndsWith("· 3,5 s", caption, StringComparison.Ordinal);
                Assert.Equal("O jogo está com a tradução de 22/10", banner.Title);
                AssertIcon(window, steps, theme, "Vnf.TextSecondary", cells[0]);
                break;
        }

        // A faixa vermelha só existe quando o teste parou, com o caminho para o registro; o cartão do que mudou vem logo depois dela.
        Assert.Equal(mode == Failed, band.IsEffectivelyVisible);
        if (mode == Failed)
        {
            Assert.Equal("O teste parou antes de terminar.", band.Title);
            Assert.Equal("Os detalhes estão em Detalhes técnicos, no registro.", band.Message);
            Assert.Equal(GameState.Blocked, band.State);
            Assert.True(Named<Button>(view, "Abrir detalhes técnicos").IsEffectivelyVisible);
            Assert.InRange(band.Bounds.Height, 63, 69);
            Assert.Equal(Bottom(view, banner) + 14, Top(view, band), 1);
        }

        var changed = Named<Border>(view, "O que mudou desde o último teste");
        Assert.Equal(16, changed.CornerRadius.TopLeft);
        Assert.Equal(Bottom(view, mode == Failed ? band : banner) + 14, Top(view, changed), 1);
        Assert.InRange(changed.Bounds.Height, 62, 70);

        var options = Named<Border>(view, "Opções do teste");
        Assert.Equal(16, options.CornerRadius.TopLeft);
        Assert.Equal(Bottom(view, changed) + 14, Top(view, options), 1);
        var switches = options.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        Assert.Equal(3, switches.Count);
        Assert.Equal(mode == Blocked ? "Erros sempre bloqueiam. Hoje há 37 avisos abertos." : "Erros sempre bloqueiam. Não há avisos abertos.", vm.AllowWarningsDescription);
        Assert.Equal("Só a cena “Capítulo 1 · Manhã de chuva” vai para o jogo; o resto fica em japonês.", vm.OpenSceneDescription);
        Assert.True(switches[0].IsEffectivelyEnabled);
        Assert.True(switches[2].IsChecked);
        Assert.False(switches[0].IsChecked);

        var details = view.GetVisualDescendants().OfType<TechnicalDetails>().Single();
        Assert.Contains("stage", details.Classes);
        Assert.False(details.IsExpanded);
        Assert.Equal(Bottom(view, options) + 14, Top(view, details), 1);
        Assert.InRange(details.Bounds.Height, 44, 48);
        var shown = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
        Assert.DoesNotContain(shown, t => t.Contains("yst0", StringComparison.Ordinal) || t.Contains("vnforge.json", StringComparison.Ordinal));
    }

    private static double Top(Control root, Control control) => control.TranslatePoint(default, root)!.Value.Y;

    private static double Bottom(Control root, Control control) => Top(root, control) + control.Bounds.Height;

    private static void InsideBanner(GameStateBanner banner, Control control)
    {
        var from = control.TranslatePoint(default, banner)!.Value;
        var to = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), banner)!.Value;
        var area = new Rect(banner.Bounds.Size).Inflate(1);
        Assert.True(area.Contains(from) && area.Contains(to), $"{control.GetType().Name} sai do cartão: {from} a {to} em {banner.Bounds}");
    }

    private static void AssertIcon(Window window, ItemsControl steps, ThemeVariant theme, string brushKey, Grid cell)
    {
        Assert.True(window.TryFindResource(brushKey, theme, out var found));
        var color = ((ISolidColorBrush)found!).Color;
        var icon = cell.GetVisualDescendants().OfType<PathIcon>().First(c => c.IsEffectivelyVisible);
        var origin = icon.TranslatePoint(default, steps)!.Value;
        var ink = PixelProbe.Capture(window, steps).CountInArea(new Rect(origin, icon.Bounds.Size), color, 96);
        Assert.True(ink > 6, $"O ícone do primeiro passo não tem a cor do estado ({ink} pixels).");
    }

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.GetValue(AutomationProperties.NameProperty) == name);

    private sealed class RefusingStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new IOException("o jogo recusou abrir");
    }

    private sealed class NoLaunchStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
