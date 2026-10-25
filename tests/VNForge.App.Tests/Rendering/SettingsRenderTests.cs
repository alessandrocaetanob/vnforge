using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using Avalonia.VisualTree;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza as três abas dos Ajustes com o projeto de demonstração, a 1440×1000, nos dois temas, para pôr ao lado do artboard do canvas:
/// <c>ajustes-ia</c>, <c>ajustes-formatacao</c> e <c>ajustes-acentuacao</c> (com cp1252 marcado, como no artboard).
/// </summary>
public sealed class SettingsRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly (int Tab, string Name)[] Tabs = [(0, "ajustes-ia"), (1, "ajustes-formatacao"), (2, "ajustes-acentuacao")];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_cada_aba_dos_Ajustes_em_1440_por_1000(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vault = new MemoryApiKeyStore();
                vault.Save("sk-ant-chave-da-demonstracao");
                var settings = new SettingsViewModel(vault, new AppSettingsFile(config.SubFolder("ajustes")), _ => null, _ => throw new InvalidOperationException("a renderização não chama a API"));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-ant-chave-da-demonstracao", ApiKeySource.Vault), settings: settings);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Settings.EstimateLoading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);

                    vm.Settings.ExchangeRateText = "5,40";
                    vm.Settings.SpendLimitText = "300";
                    vm.Settings.WarnAboveText = "25";

                    foreach (var (tab, name) in Tabs)
                    {
                        vm.Settings.SelectedTab = tab;
                        if (tab == 2)
                        {
                            vm.Settings.SelectedEncoding = vm.Settings.Encodings.Single(e => e.Id == "cp1252");
                        }

                        ScreenRenderer.Settle(window);
                        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                        ScreenRenderer.Save(window, path);

                        using var png = new Bitmap(path);
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);

                        if (tab == 0)
                        {
                            ConferirAbaIaEGasto(window, theme);
                        }
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

    /// <summary>
    /// Confere a aba "IA e gasto" pela tinta em retângulos conhecidos, sem contar faixas de texto (a fonte muda de um sistema para outro): os três cartões
    /// de qualidade lado a lado e dentro da janela, o marcado com a borda do accent, a barra do orçamento com o gasto e a marca do limite.
    /// </summary>
    private static void ConferirAbaIaEGasto(Window window, ThemeVariant theme)
    {
        var cards = window.GetVisualDescendants().OfType<QualityCard>().Where(c => c.IsEffectivelyVisible).OrderBy(c => c.TranslatePoint(default, window)!.Value.X).ToList();
        Assert.Equal(3, cards.Count);
        Assert.All(cards, c => Assert.True(c.Bounds.Width > 100 && c.Bounds.Height > 60, $"{c.Title}: {c.Bounds}"));
        var lefts = cards.Select(c => c.TranslatePoint(default, window)!.Value).ToList();
        Assert.True(lefts[0].X + cards[0].Bounds.Width <= lefts[1].X + 1 && lefts[1].X + cards[1].Bounds.Width <= lefts[2].X + 1, "cartões sobrepostos");
        Assert.True(lefts[2].X + cards[2].Bounds.Width <= window.Bounds.Width);
        Assert.All(cards, c => Assert.True(c.ShowPrice, $"{c.Title} sem estimativa"));

        Assert.True(window.TryFindResource("Vnf.Accent", theme, out var found));
        var accent = ((ISolidColorBrush)found!).Color;
        var marked = cards.Single(c => c.IsChecked == true);
        var cardProbe = PixelProbe.Capture(window, marked);
        var borderTop = new Rect(8, 0, marked.Bounds.Width - 16, 3);
        Assert.True(cardProbe.CountInArea(borderTop, accent, 24) > marked.Bounds.Width / 2, "borda do accent no cartão marcado");
        var unmarked = cards.First(c => c.IsChecked != true);
        Assert.Equal(0, PixelProbe.Capture(window, unmarked).CountInArea(borderTop, accent, 24));

        var bar = window.GetVisualDescendants().OfType<BudgetBar>().Single(b => b.IsEffectivelyVisible);
        Assert.Equal(300.0, bar.Limit);
        Assert.True(bar.Bounds.Width > 200 && bar.Bounds.Height >= BudgetBar.BarHeight);
        Assert.True(window.TryFindResource("Vnf.TextPrimary", theme, out var textBrush));
        var mark = ((ISolidColorBrush)textBrush!).Color;
        var markArea = new Rect(0, 0, bar.Bounds.Width, bar.Bounds.Height);
        Assert.True(PixelProbe.Capture(window, bar).CountInArea(markArea, mark, 24) >= BudgetBar.BarHeight, "marca do limite na barra");
    }
}
