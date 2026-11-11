using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
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
/// <c>-falhou</c>) e com um projeto que nunca testou (<c>-sem-teste</c>, os cinco passos cinza). A conferência de cor olha a tinta em retângulos conhecidos (o ponto de estado, o ícone do primeiro passo),
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
                        // O prazo é em tempo, não em voltas: no Linux carregado cada volta demora mais que o Task.Delay pede.
                        var deadline = Stopwatch.StartNew();
                        while ((build.LastTestSteps.Count == 0 || build.LastTestSteps[^1].State != TestStepState.Failed) && deadline.Elapsed < TimeSpan.FromSeconds(15))
                        {
                            await Task.Delay(50, TestContext.Current.CancellationToken);
                            ScreenRenderer.Settle(window);
                        }

                        Assert.True(
                            build.LastTestSteps.Count > 0 && build.LastTestSteps[^1].State == TestStepState.Failed,
                            $"O último passo do teste não chegou a Failed em 15 s: [{string.Join(", ", build.LastTestSteps.Select(s => s.State))}]");
                    }

                    ScreenRenderer.Settle(window);
                    var path = RenderOutput.PathFor(ScreenRenderer.FileName(fileName, theme));
                    ScreenRenderer.Save(window, path);
                    var view = window.GetVisualDescendants().OfType<BuildView>().Single();
                    Inspect(window, view, build, theme, mode);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(Window window, BuildView view, BuildViewModel vm, ThemeVariant theme, string mode)
    {
        var banner = view.GetVisualDescendants().OfType<GameStateBanner>().First();
        Assert.Equal(mode == Blocked ? GameState.Blocked : mode == NeverTested ? GameState.Original : GameState.Translated, banner.State);

        var dot = banner.GetVisualDescendants().OfType<Shapes.Ellipse>().Single(e => e.Name == "PART_Dot");
        var fill = ((ISolidColorBrush)dot.Fill!).Color;
        var origin = dot.TranslatePoint(default, banner) ?? throw new InvalidOperationException("O ponto não está na faixa.");
        var probe = PixelProbe.Capture(window, banner);
        Assert.True(probe.CountInArea(new Rect(origin, dot.Bounds.Size), fill, 24) > 40, "O ponto de estado não tem a cor do estado.");

        var back = Named<Button>(view, "Voltar ao jogo original");
        Assert.True(back.IsEffectivelyVisible);
        Assert.Equal(mode is Translated or Failed, back.IsEffectivelyEnabled);
        Assert.Contains("danger", back.Classes);
        InsideBanner(banner, back);

        var test = Named<Button>(view, "Testar no jogo");
        Assert.Equal(mode != Blocked, test.IsEffectivelyEnabled);
        Assert.True(test.Classes.Contains("lg"), "O botão principal é o grande.");
        InsideBanner(banner, test);
        var corner = test.TranslatePoint(new Point(test.Bounds.Width - 1, test.Bounds.Height - 1), window)!.Value;
        Assert.True(new Rect(window.Bounds.Size).Contains(corner), "O botão principal sai da janela.");

        // Uma tela, um botão de destaque: Ver os erros quando trava, Testar no jogo quando pode.
        var showErrors = Named<Button>(view, "Ver os erros");
        Assert.Equal(mode == Blocked, showErrors.IsEffectivelyVisible);
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
        Assert.All(rects, r => Assert.True(r.Width > 100, $"Passo estreito: {r}"));
        for (var i = 1; i < rects.Count; i++)
        {
            Assert.True(rects[i - 1].Right <= rects[i].X + 1, "Passos sobrepostos.");
        }

        var texts = steps.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        var caption = Named<TextBlock>(view, "Resumo dos passos").Text;
        switch (mode)
        {
            case Blocked:
                Assert.True(vm.LastTestSteps[0].IsBlocked);
                Assert.All(vm.LastTestSteps.Skip(1), r => Assert.True(r.IsPending));
                Assert.Contains("2 erros travam", texts);
                AssertIcon(window, steps, theme, cells[0]);
                break;
            case Failed:
                Assert.Equal(
                    [TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Failed],
                    vm.LastTestSteps.Select(r => r.State));
                Assert.Contains("O jogo não abriu", texts);
                Assert.Equal("O último teste falhou.", caption);
                AssertIcon(window, steps, theme, cells[4]);
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
                AssertIcon(window, steps, theme, cells[0]);
                break;
        }

        // A faixa vermelha só existe quando o teste parou.
        var band = view.GetVisualDescendants().OfType<GameStateBanner>().Single(b => b.GetValue(AutomationProperties.NameProperty) == "Mensagem de erro");
        Assert.Equal(mode == Failed, band.IsEffectivelyVisible);

        var details = view.GetVisualDescendants().OfType<TechnicalDetails>().Single();
        Assert.False(details.IsExpanded);
        var shown = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
        Assert.DoesNotContain(shown, t => t.Contains("yst0", StringComparison.Ordinal) || t.Contains("vnforge.json", StringComparison.Ordinal));
    }

    private static void InsideBanner(GameStateBanner banner, Control control)
    {
        var from = control.TranslatePoint(default, banner)!.Value;
        var to = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), banner)!.Value;
        var area = new Rect(banner.Bounds.Size).Inflate(1);
        Assert.True(area.Contains(from) && area.Contains(to), $"{control.GetType().Name} sai do cartão: {from} a {to} em {banner.Bounds}");
    }

    /// <summary>No quadro os ícones dos passos são neutros (text-secondary); o erro vem no texto vermelho sob o passo.</summary>
    private static void AssertIcon(Window window, ItemsControl steps, ThemeVariant theme, Grid cell)
    {
        Assert.True(window.TryFindResource("Vnf.TextSecondary", theme, out var found));
        var color = ((ISolidColorBrush)found!).Color;
        var icon = cell.GetVisualDescendants().OfType<PathIcon>().First(c => c.IsEffectivelyVisible);
        var origin = icon.TranslatePoint(default, steps)!.Value;
        var ink = PixelProbe.Capture(window, steps).CountInArea(new Rect(origin, icon.Bounds.Size), color, 96);
        Assert.True(ink > 6, $"O ícone do passo não tem a cor do estado ({ink} pixels).");
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
