using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza as telas do app com o projeto de demonstração para PNG, a 1440×1000, nos dois temas, para pôr ao lado do artboard nos PRs de tela.
/// Os arquivos vão para <c>VNFORGE_RENDER_DIR</c> (ou uma pasta temporária) com o nome <c>tela-escuro.png</c> e <c>tela-claro.png</c>.
/// </summary>
public sealed class ScreenRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly (NavKey Key, string Name)[] Screens =
    [
        (NavKey.Start, "inicio"),
        (NavKey.Dashboard, "painel"),
        (NavKey.Editor, "editor"),
        (NavKey.Conference, "conferir"),
        (NavKey.Build, "build"),
        (NavKey.Settings, "ajustes"),
    ];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_cada_tela_em_1440_por_1000(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;

                    foreach (var (key, name) in Screens)
                    {
                        vm.SelectedNav = vm.Nav.Single(n => n.Key == key);
                        await Pending(vm, key);
                        ScreenRenderer.Settle(window);

                        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                        ScreenRenderer.Save(window, path);

                        using var png = new Bitmap(path);
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task Pending(MainWindowViewModel vm, NavKey key)
    {
        switch (key)
        {
            case NavKey.Dashboard:
                await vm.Dashboard.ReloadAsync();
                break;
            case NavKey.Build:
                await vm.Build.Loading;
                break;
            case NavKey.Conference:
                await vm.ConferenceLoading;
                break;
        }
    }
}

/// <summary>A pasta do projeto de demonstração, gravada uma vez por classe de teste e apagada no fim.</summary>
public sealed class DemoFolderFixture : IDisposable
{
    private readonly TempFolder temp = new();

    public DemoFolderFixture() => Root = DemoProject.Create(temp.SubFolder("projeto"));

    public string Root { get; }

    public void Dispose() => temp.Dispose();
}
