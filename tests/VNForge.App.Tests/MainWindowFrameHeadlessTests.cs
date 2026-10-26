using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Abre a moldura real (barra de título, trilho, barra de status e paleta) para pegar erro de XAML e de atalho que os testes de ViewModel não veem.</summary>
public class MainWindowFrameHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [Fact]
    public async Task Ctrl_K_abre_a_paleta_as_setas_e_o_Enter_executam_e_o_Esc_fecha()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Untranslated);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new MainWindow { Width = 1440, Height = 900, DataContext = vm };
                try
                {
                    window.Show();
                    Settle(window);
                    var palette = window.GetVisualDescendants().OfType<CommandPaletteView>().Single();
                    Assert.False(palette.IsVisible);

                    window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
                    Settle(window);
                    Assert.True(vm.Palette.IsOpen);
                    Assert.True(palette.IsVisible);

                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Settle(window);
                    Assert.False(vm.Palette.IsOpen);
                    Assert.False(palette.IsVisible);

                    vm.Palette.Open();
                    Settle(window);
                    var painel = vm.Palette.Results.ToList().FindIndex(r => r.Title == "Painel");
                    for (var i = 0; i < painel; i++)
                    {
                        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    }

                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    Settle(window);
                    Assert.False(vm.Palette.IsOpen);
                    Assert.Equal(NavKey.Dashboard, vm.SelectedNav?.Key);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Barra_de_titulo_trilho_e_barra_de_status_mostram_os_dados_do_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[0] = units[0] with { Translation = "duas  palavras" };
        folder.SaveText("yst00001", units);
        var vm = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now)));
        await vm.Start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new MainWindow { Width = 1440, Height = 900, DataContext = vm };
                try
                {
                    window.Show();
                    Settle(window);
                    var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

                    Assert.Contains("Buscar linhas, scripts e comandos", texts);
                    Assert.Contains("Ctrl+K", texts);
                    Assert.Contains("Minha VN", texts);
                    Assert.Contains("Conferir", texts);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "1" && t.DataContext is NavItemViewModel { Key: NavKey.Conference });
                    Assert.Contains("Glossário", texts);
                    Assert.Contains("Ajustes", texts);
                    Assert.DoesNotContain("Busca", texts);
                    Assert.Contains("Acentos: sem acentos", texts);
                    Assert.Contains("54 colunas · 3 linhas", texts);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
