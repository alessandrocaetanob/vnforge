using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Editor e o Modo revisão no design system v3 (#546), nos dois temas, a 1440×1000 e a 1366×768, com o projeto de demonstração: a fala 0495 selecionada, como o quadro <c>#issue-546</c>.
/// Os PNGs (<c>editor-v3-*</c>) vão para <c>VNFORGE_RENDER_DIR</c>.
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

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task O_Editor_no_v3_aparece_com_a_fala_0495_selecionada(string themeName, int width, int height)
    {
        var demo = demoFolder;
        await Run(
            async () =>
            {
                using var config = new TempFolder();
                var (window, _) = await OpenDemo(config, demo.Root, Of(themeName), width, height);
                try
                {
                    ScreenRenderer.Save(window, RenderOutput.PathFor($"editor-v3-{width}-{(themeName == "Light" ? "claro" : "escuro")}.png"));
                }
                finally
                {
                    window.Close();
                }
            });
    }
}
