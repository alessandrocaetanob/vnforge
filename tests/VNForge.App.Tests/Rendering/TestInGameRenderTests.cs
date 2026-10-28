using Avalonia;
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
/// Renderiza "Testar no jogo" em 1440×1000 nos dois temas, com o jogo traduzido (<c>testar-no-jogo-traduzido</c>), com a Conferência travando o teste
/// (<c>testar-no-jogo-travado</c>) e com um projeto que nunca testou (<c>testar-no-jogo-sem-teste</c>, os cinco passos cinza). A conferência olha a tinta
/// em retângulos conhecidos (o ponto de estado da faixa, o ícone do primeiro passo), não faixas de texto, porque a fonte da CI em Linux não é a do Windows.
/// Os PNGs saem em <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class TestInGameRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const string Translated = "traduzido";
    private const string Blocked = "travado";
    private const string NeverTested = "sem-teste";
    private const string Failed = "falhou";

    public static TheoryData<string, string> Cases => new()
    {
        { "Dark", Translated },
        { "Light", Translated },
        { "Dark", Blocked },
        { "Light", Blocked },
        { "Dark", NeverTested },
        { "Light", NeverTested },
        { "Dark", Failed },
        { "Light", Failed },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Renderiza_a_tela_testar_no_jogo(string themeName, string mode) => RenderAsync(themeName, mode, ScreenRenderer.Width, ScreenRenderer.Height, string.Empty);

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public Task Com_erros_tudo_cabe_na_janela_de_1366_por_768(string themeName) => RenderAsync(themeName, Blocked, 1366, 768, "-1366");

    private async Task RenderAsync(string themeName, string mode, int width, int height, string suffix)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"));
        var qa = new DemoQaRule { Enabled = mode == Blocked };
        var vm = new BuildViewModel(new BackgroundTaskService(), [new DemoBuildEngine()], mode == Failed ? new RefusingStarter() : new NoLaunchStarter(), new QaEngine([qa]))
        {
            Project = DemoProject.Summary(root),
        };
        if (mode == Translated)
        {
            var steps = new[] { GameTestStep.Check, GameTestStep.Build, GameTestStep.Backup, GameTestStep.Inject, GameTestStep.Open };
            var times = new long[] { 400, 2_100, 300, 600, 150 };
            GameStateFile.Save(ProjectFolder.Open(root), new GameStateFile(
                GameStateFile.CurrentVersion,
                new GameInGame(GameTestKind.Translation, new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316", null, null),
                new GameLastTest(new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), GameTestKind.Translation, [.. steps.Select((s, i) => new GameTestStepTime(s, times[i]))])));
        }

        await fixture.Session.Dispatch(
            async () =>
            {
                await vm.ReloadAsync();
                if (mode == Failed)
                {
                    await vm.TestCommand.ExecuteAsync(null);
                }

                var path = RenderOutput.PathFor(ScreenRenderer.FileName("testar-no-jogo-" + mode + suffix, theme));
                ScreenRenderer.Render(new BuildView { DataContext = vm }, theme, path, width, height, inspect: window => Inspect(window, vm, theme, mode));
                Assert.True(new FileInfo(path).Length > 10_000);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(Window window, BuildViewModel vm, ThemeVariant theme, string mode)
    {
        var banner = window.GetVisualDescendants().OfType<GameStateBanner>().Single();
        Assert.Equal(mode == Blocked ? GameState.Blocked : GameState.Translated, banner.State);

        var dot = banner.GetVisualDescendants().OfType<Shapes.Ellipse>().Single(e => e.Name == "PART_Dot");
        var fill = ((ISolidColorBrush)dot.Fill!).Color;
        var origin = dot.TranslatePoint(default, banner) ?? throw new InvalidOperationException("O ponto não está na faixa.");
        var probe = PixelProbe.Capture(window, banner);
        Assert.True(probe.CountInArea(new Rect(origin, dot.Bounds.Size), fill, 24) > 40, "O ponto de estado não tem a cor do estado.");

        var back = Named<Button>(window, "Voltar ao jogo original");
        Assert.True(back.IsEffectivelyVisible);
        Assert.True(back.IsEffectivelyEnabled);
        Assert.False(back.Classes.Contains("accent"), "Voltar ao jogo original é neutro.");
        InsideBanner(banner, back);

        var test = Named<Button>(window, "Testar no jogo");
        Assert.Equal(mode != Blocked, test.IsEffectivelyEnabled);
        Assert.True(test.Classes.Contains("lg"), "O botão principal é o grande.");
        InsideBanner(banner, test);
        var corner = test.TranslatePoint(new Point(test.Bounds.Width - 1, test.Bounds.Height - 1), window)!.Value;
        Assert.True(new Rect(window.Bounds.Size).Contains(corner), "O botão principal sai da janela.");

        var showErrors = Named<Button>(window, "Ver os erros");
        Assert.Equal(mode == Blocked, showErrors.IsEffectivelyVisible);
        Assert.False(showErrors.Classes.Contains("accent"), "Ver os erros é neutro: uma tela, um botão de destaque.");
        var accents = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("accent")).ToList();
        Assert.Equal([test], accents);
        Assert.True(Named<Button>(window, "Testar só os acentos").IsEffectivelyEnabled);

        var steps = Named<ItemsControl>(window, "Passos do último teste");
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
        var caption = Named<TextBlock>(window, "Resumo dos passos").Text;
        switch (mode)
        {
            case Blocked:
                Assert.True(vm.LastTestSteps[0].IsBlocked);
                Assert.All(vm.LastTestSteps.Skip(1), r => Assert.True(r.IsPending));
                Assert.Contains("2 erros travam", texts);
                AssertIcon(window, steps, theme, "Vnf.SignalErrorFg", cells[0]);
                break;
            case Failed:
                Assert.Equal(
                    [TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Done, TestStepState.Failed],
                    vm.LastTestSteps.Select(r => r.State));
                Assert.Contains("O jogo não abriu", texts);
                Assert.Equal("O último teste falhou.", caption);
                AssertIcon(window, steps, theme, "Vnf.SignalErrorFg", cells[4]);
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
                AssertIcon(window, steps, theme, "Vnf.StatusApprovedFg", cells[0]);
                break;
        }

        var details = window.GetVisualDescendants().OfType<TechnicalDetails>().Single();
        Assert.False(details.IsExpanded);
        var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToList();
        Assert.DoesNotContain(shown, t => t.Contains("yst0", StringComparison.Ordinal) || t.Contains("vnforge.json", StringComparison.Ordinal));
    }

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
        var ink = PixelProbe.Capture(window, steps).CountInArea(new Rect(origin, icon.Bounds.Size), color, 24);
        Assert.True(ink > 12, $"O ícone do primeiro passo não tem a cor do estado ({ink} pixels).");
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

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
