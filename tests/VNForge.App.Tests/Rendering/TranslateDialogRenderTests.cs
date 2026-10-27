using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza a janela "Traduzir com IA" sobre o Editor do projeto de demonstração, a 1440×1000, nos dois temas, para pôr ao lado do artboard:
/// <c>traduzir-com-ia</c> (dentro do limite) e <c>traduzir-com-ia-acima-do-limite</c> (o botão desligado e o aviso com a troca de qualidade).
/// </summary>
public sealed class TranslateDialogRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private Task<int> OnUi(Func<MainWindowViewModel, Window, Task> test, ThemeVariant theme) => fixture.Session.Dispatch<int>(
        async () =>
        {
            using var config = new TempFolder();
            var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(Now));
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
                await vm.Editor.Loading;
                vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                vm.Editor.SelectedScript ??= vm.Editor.Scripts.First();
                vm.Settings.ExchangeRateText = "5,40";
                vm.Settings.SpendLimitText = "300";
                vm.Settings.WarnAboveText = "250";
                await test(vm, window);
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        TestContext.Current.CancellationToken);

    private static async Task OpenDialogAsync(MainWindowViewModel vm, Window window)
    {
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        ScreenRenderer.Settle(window);
    }

    private static Control Surface(Window window) =>
        OverlayLayer.GetOverlayLayer(window)!.Children.OfType<Control>().Single(c => c.GetVisualDescendants().OfType<QualityCard>().Any());

    private static void Save(Window window, string name, ThemeVariant theme)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    private static Border Card(Control surface) => surface.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Dialog");

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_janela_dentro_do_limite(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window) =>
            {
                await OpenDialogAsync(vm, window);

                var surface = Surface(window);
                Assert.Equal(3, surface.GetVisualDescendants().OfType<QualityCard>().Count());
                Assert.Equal(3, surface.GetVisualDescendants().OfType<RadioButton>().Count(r => r is not QualityCard));
                var bar = Assert.Single(surface.GetVisualDescendants().OfType<BudgetBar>());
                Assert.True(bar.Ask > 0 && bar.Limit > 0 && !bar.IsOver);
                Assert.Empty(window.OwnedWindows);
                Assert.Same(window, TopLevel.GetTopLevel(surface));

                // Qualidade em vez de modelo: nenhum cartão diz Haiku, Sonnet ou Opus.
                var texts = surface.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.DoesNotContain(texts, t => t is "Haiku" or "Sonnet" or "Opus");

                // A cena aberta não tem linha pendente: o cartão fica desligado, apagado e sem escolha, e o padrão é o escopo seguinte com linhas.
                var scopes = surface.GetVisualDescendants().OfType<RadioButton>().Where(r => r is not QualityCard).ToList();
                Assert.False(scopes[0].IsEffectivelyEnabled);
                Assert.Equal(0.5, scopes[0].Opacity);
                Assert.NotEqual(true, scopes[0].IsChecked);
                Assert.All(scopes.Skip(1), r => Assert.True(r.IsEffectivelyEnabled));
                Assert.Contains("Nada para traduzir aqui", texts);
                Assert.Equal(true, scopes[1].IsChecked);

                // O cartão pinta o próprio fundo por cima do véu: o canto do cartão difere do fundo da página no mesmo ponto.
                var card = Card(surface);
                var probe = PixelProbe.Capture(window, card);
                var inside = probe.At(probe.Width / 2, 4);
                var corner = PixelProbe.Capture(window, window).At(8, 8);
                Assert.NotEqual(inside, corner);

                Save(window, "traduzir-com-ia", theme);
            },
            theme);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_pedido_acima_do_limite_com_a_troca_de_qualidade(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window) =>
            {
                await OpenDialogAsync(vm, window);
                vm.Translate.SelectedScope = vm.Translate.Scopes.Single(s => s.Scope == TranslationScope.Game);
                var balanced = vm.Translate.Qualities.Single(q => q.Quality == TranslationQuality.Balanced).Price;
                var careful = vm.Translate.Qualities.Single(q => q.Quality == TranslationQuality.Careful).Price;
                Assert.True(balanced < careful);

                vm.Translate.IsOpen = false;
                vm.Settings.SpendLimitText = MoneyParser.Format(Math.Round((decimal)vm.Translate.SpentBrl + ((balanced + careful) / 2m), 2));
                await OpenDialogAsync(vm, window);
                vm.Translate.SelectedScope = vm.Translate.Scopes.Single(s => s.Scope == TranslationScope.Game);
                vm.Translate.SelectedQuality = vm.Translate.Qualities.Single(q => q.Quality == TranslationQuality.Careful);
                ScreenRenderer.Settle(window);

                Assert.True(vm.Translate.IsOverLimit);
                Assert.True(vm.Translate.CanSwitchToFit);
                var surface = Surface(window);
                Assert.True(Assert.Single(surface.GetVisualDescendants().OfType<BudgetBar>()).IsOver);
                var start = surface.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.Translate.StartCommand);
                Assert.False(start.IsEffectivelyEnabled);
                Assert.Contains(surface.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Translate.SwitchToFitCommand && b.IsEffectivelyVisible);

                Save(window, "traduzir-com-ia-acima-do-limite", theme);

                vm.Translate.SwitchToFitCommand.Execute(null);
                ScreenRenderer.Settle(window);
                Assert.True(start.IsEffectivelyEnabled);
            },
            theme);
    }

    [Fact]
    public async Task Escape_fecha_a_janela_sem_abrir_outra()
    {
        await OnUi(
            async (vm, window) =>
            {
                await OpenDialogAsync(vm, window);
                var layer = OverlayLayer.GetOverlayLayer(window)!;
                Assert.True(vm.Translate.IsOpen);
                Assert.Empty(window.OwnedWindows);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                ScreenRenderer.Settle(window);

                Assert.False(vm.Translate.IsOpen);
                Assert.DoesNotContain(layer.Children.OfType<Control>(), c => c.GetVisualDescendants().OfType<QualityCard>().Any());
                Assert.Empty(window.OwnedWindows);
            },
            ThemeVariant.Dark);
    }
}
