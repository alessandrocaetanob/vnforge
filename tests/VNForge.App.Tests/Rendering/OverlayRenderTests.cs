using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>Renderiza o aviso curto e o mapa de atalhos sobre o Editor com o projeto de demonstração (1440×1000, tema escuro).</summary>
public sealed class OverlayRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    [Fact]
    public Task Aviso_e_mapa_de_atalhos_sobre_o_editor() => fixture.Session.Dispatch<int>(
        async () =>
        {
            using var config = new TempFolder();
            var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(Now));
            var toasts = new ToastService(new FakeTimeProvider(), a => a());
            var vm = new MainWindowViewModel(start, toasts: toasts);
            var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = ThemeVariant.Dark, DataContext = vm };
            try
            {
                window.Show();
                await start.OpenFolderCommand.ExecuteAsync(null);
                await vm.ProjectOpening;
                await vm.Editor.Loading;
                vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                toasts.Show("Linha 12 aprovada.", ToastKind.Success, key: "line-save", undo: () => { });
                ScreenRenderer.Settle(window);
                Save(window, "aviso");

                vm.ToggleShortcutMapCommand.Execute(null);
                ScreenRenderer.Settle(window);
                Save(window, "atalhos");
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        TestContext.Current.CancellationToken);

    private static void Save(Window window, string name)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, ThemeVariant.Dark));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }
}
