using Avalonia.Controls;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// As capturas do Editor e do Modo revisão no design system v3 (#546), nos dois temas, a 1440×1000 e a 1366×768, com o projeto de demonstração e a fala 0495 selecionada, para pôr ao lado do quadro <c>#issue-546</c>.
/// Não mede nada: a conferência é a captura em <c>docs/design/telas/v6</c> (<c>VNFORGE_RENDER_DIR</c>) ao lado do quadro.
/// </summary>
public sealed class EditorV3RenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, int, int> Cases => new()
    {
        { "Dark", 1440, 1000 },
        { "Light", 1440, 1000 },
        { "Dark", 1366, 768 },
        { "Light", 1366, 768 },
    };

    private static ThemeVariant Of(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private Task<int> Run(Func<Task> body) =>
        fixture.Session.Dispatch<int>(
            async () =>
            {
                await body();
                return 0;
            },
            TestContext.Current.CancellationToken);

    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> OpenDemo(TempFolder config, string demoRoot, ThemeVariant theme, int width, int height)
    {
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoRoot), new FixedClock(Now));
        var editor = new EditorViewModel(scriptLabel: DemoProject.Label, clock: new FixedClock(Now));
        var vm = new MainWindowViewModel(start) { Editor = editor };
        var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectOpening;
        await editor.Loading;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
        Assert.True(await editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495"));
        ScreenRenderer.Settle(window);
        return (window, vm);
    }

    private static string Suffix(string themeName, int width) => $"{width}-{(themeName == "Light" ? "claro" : "escuro")}";

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_Editor_no_v3_gera_a_captura_com_a_fala_0495_selecionada(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-v6-{Suffix(themeName, width)}.png"));
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_Modo_revisao_no_v3_gera_a_captura(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, vm) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    vm.Editor.StartReviewCommand.Execute(null);
                    ScreenRenderer.Settle(window);
                    Assert.True(vm.Editor.IsReviewing);
                    ScreenRenderer.Save(window, RenderOutput.PathFor($"modo-revisao-v6-{Suffix(themeName, width)}.png"));
                }
                finally
                {
                    window.Close();
                }
            });
    }
}
