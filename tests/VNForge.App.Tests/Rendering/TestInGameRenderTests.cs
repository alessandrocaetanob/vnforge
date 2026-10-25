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
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using Shapes = Avalonia.Controls.Shapes;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza "Testar no jogo" em 1440×1000 nos dois temas, com o jogo traduzido (<c>testar-no-jogo-traduzido</c>) e com a Conferência travando o teste
/// (<c>testar-no-jogo-travado</c>). A conferência olha a tinta em retângulos conhecidos (o ponto de estado da faixa), não faixas de texto, porque a fonte
/// da CI em Linux não é a do Windows. Os PNGs saem em <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class TestInGameRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string, bool> Cases => new()
    {
        { "Dark", false },
        { "Light", false },
        { "Dark", true },
        { "Light", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Renderiza_a_tela_testar_no_jogo(string themeName, bool blocked)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"));
        var qa = new DemoQaRule { Enabled = blocked };
        var vm = new BuildViewModel(new BackgroundTaskService(), [new DemoBuildEngine()], new NoLaunchStarter(), new QaEngine([qa]))
        {
            Project = DemoProject.Summary(root),
        };
        if (!blocked)
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
                var name = blocked ? "testar-no-jogo-travado" : "testar-no-jogo-traduzido";
                var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                ScreenRenderer.Render(new BuildView { DataContext = vm }, theme, path, inspect: window => Inspect(window, vm, blocked));
                Assert.True(new FileInfo(path).Length > 10_000);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Inspect(Window window, BuildViewModel vm, bool blocked)
    {
        var banner = window.GetVisualDescendants().OfType<GameStateBanner>().Single();
        Assert.Equal(blocked ? GameState.Blocked : GameState.Translated, banner.State);

        var dot = banner.GetVisualDescendants().OfType<Shapes.Ellipse>().Single(e => e.Name == "PART_Dot");
        var fill = ((ISolidColorBrush)dot.Fill!).Color;
        var origin = dot.TranslatePoint(default, banner) ?? throw new InvalidOperationException("O ponto não está na faixa.");
        var probe = PixelProbe.Capture(window, banner);
        Assert.True(probe.CountInArea(new Rect(origin, dot.Bounds.Size), fill, 24) > 40, "O ponto de estado não tem a cor do estado.");

        var back = Named<Button>(window, "Voltar ao jogo original");
        Assert.True(back.IsEffectivelyVisible);
        Assert.True(back.IsEffectivelyEnabled);
        Assert.True(new Rect(banner.Bounds.Size).Contains(back.TranslatePoint(default, banner)!.Value));

        var test = Named<Button>(window, "Testar no jogo");
        Assert.Equal(!blocked, test.IsEffectivelyEnabled);
        Assert.Equal(blocked, Named<Button>(window, "Ver os erros").IsEffectivelyVisible);
        Assert.True(Named<Button>(window, "Testar só os acentos").IsEffectivelyEnabled);

        var steps = Named<ItemsControl>(window, "Passos do último teste");
        Assert.Equal(blocked ? 0 : 5, vm.LastTestSteps.Count);
        Assert.Equal(!blocked, steps.IsEffectivelyVisible);

        var details = window.GetVisualDescendants().OfType<TechnicalDetails>().Single();
        Assert.False(details.IsExpanded);
    }

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    private sealed class NoLaunchStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
