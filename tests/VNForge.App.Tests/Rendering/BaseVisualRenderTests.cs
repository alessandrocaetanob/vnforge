using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A base visual da v4 nos dois temas: o contraste do botão principal e do segmento marcado em todos os estados, os títulos sem corte, o selo do
/// trilho que só conta erros, as sobrancelhas na fonte da interface e o aviso da chave na barra de status. O contraste lê a cor que o
/// <c>ContentPresenter</c> do modelo realmente pinta (é nele que o FluentTheme escreve) e confere os pixels do quadro.
/// </summary>
public sealed class BaseVisualRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static Color Token(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) && value is Color color
            ? color
            : throw new InvalidOperationException($"O token {key} não existe no tema {window.ActualThemeVariant}.");

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    internal static double Contrast(Color a, Color b)
    {
        var (light, dark) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (light + 0.05) / (dark + 0.05);
    }

    private static Color SolidColor(IBrush? brush) =>
        brush is ISolidColorBrush solid ? solid.Color : throw new InvalidOperationException($"O pincel {brush} não é uma cor sólida.");

    private static ContentPresenter Presenter(Control control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    private Task OnUi(Action test) => fixture.Session.Dispatch(test, TestContext.Current.CancellationToken);

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Botao_principal_tem_4_5_para_1_de_contraste_em_cada_estado_e_desligado_nao_parece_ligado(string themeName)
    {
        var theme = Variant(themeName);
        await OnUi(() =>
        {
            var normal = new Button { Content = "Criar projeto", Classes = { "accent" } };
            var hover = new Button { Content = "Criar projeto", Classes = { "accent" } };
            var pressed = new Button { Content = "Criar projeto", Classes = { "accent" } };
            var disabled = new Button { Content = "Avançar", Classes = { "accent" }, IsEnabled = false };
            var neutral = new Button { Content = "Voltar" };
            ((IPseudoClasses)hover.Classes).Set(":pointerover", true);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            foreach (var button in new[] { neutral, normal, hover, pressed, disabled })
            {
                row.Children.Add(button);
            }

            ScreenRenderer.Render(row, theme, RenderOutput.PathFor(ScreenRenderer.FileName("base-botao-principal", theme)), 640, 120, window =>
            {
                var accent = Token(window, "Vnf.AccentColor");
                var accentHover = Token(window, "Vnf.AccentHoverColor");
                var onAccent = Token(window, "Vnf.OnAccentColor");

                var center = pressed.TranslatePoint(new Point(pressed.Bounds.Width / 2, pressed.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                ScreenRenderer.Settle(window);
                Assert.True(pressed.IsPressed);

                foreach (var (button, background) in new[] { (normal, accent), (hover, accentHover), (pressed, accentHover) })
                {
                    var presenter = Presenter(button);
                    Assert.Equal(background, SolidColor(presenter.Background));
                    Assert.Equal(onAccent, SolidColor(presenter.Foreground));
                    var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                    Assert.Equal(onAccent, SolidColor(label.Foreground));
                    Assert.True(Contrast(SolidColor(presenter.Foreground), SolidColor(presenter.Background)) >= 4.5, $"{themeName}: contraste do botão {button.Classes}");

                    var probe = PixelProbe.Capture(window, button);
                    var area = new Rect(0, 0, probe.Width, probe.Height);
                    Assert.True(probe.CountInArea(area, background, 2) > area.Width * area.Height * 0.5, $"{themeName}: o fundo do botão não é o accent");
                }

                window.MouseUp(center, MouseButton.Left);

                var off = Presenter(disabled);
                Assert.Equal(Token(window, "Vnf.BgInsetColor"), SolidColor(off.Background));
                Assert.Equal(Token(window, "Vnf.TextTertiaryColor"), SolidColor(off.Foreground));
                var offProbe = PixelProbe.Capture(window, disabled);
                var offArea = new Rect(0, 0, offProbe.Width, offProbe.Height);
                Assert.Equal(0, offProbe.CountInArea(offArea, [accent, accentHover], 2));
                Assert.True(offProbe.CountInArea(offArea, Token(window, "Vnf.BgInsetColor"), 2) > offArea.Width * offArea.Height * 0.5);
            });
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Segmento_marcado_e_uma_pilula_neutra_com_4_5_para_1_de_contraste_e_sem_accent(string themeName)
    {
        var theme = Variant(themeName);
        await OnUi(() =>
        {
            var list = new ListBox
            {
                Classes = { "seg" },
                ItemsSource = new[] { "Primeira", "Terceira" },
                SelectedIndex = 0,
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Top,
            };

            ScreenRenderer.Render(list, theme, RenderOutput.PathFor(ScreenRenderer.FileName("base-segmentado", theme)), 400, 100, window =>
            {
                var accent = Token(window, "Vnf.AccentColor");
                var card = Token(window, "Vnf.BgCardColor");
                var text = Token(window, "Vnf.TextPrimaryColor");
                var items = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                Assert.Equal(2, items.Count);

                var selected = Presenter(items[0]);
                Assert.Equal(card, SolidColor(selected.Background));
                Assert.Equal(text, SolidColor(selected.Foreground));
                Assert.True(Contrast(text, card) >= 4.5);
                var label = items[0].GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.Equal(text, SolidColor(label.Foreground));

                var on = PixelProbe.Capture(window, items[0]);
                Assert.True(on.CountInArea(new Rect(0, 0, on.Width, on.Height), card, 2) > on.Width * on.Height * 0.5);
                Assert.Equal(0, on.CountInArea(new Rect(0, 0, on.Width, on.Height), accent, 2));
                var off = PixelProbe.Capture(window, items[1]);
                Assert.Equal(0, off.CountInArea(new Rect(0, 0, off.Width, off.Height), accent, 2));
            });
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Titulo_de_pagina_nao_corta_a_descendente_e_a_sobrancelha_usa_a_fonte_da_interface(string themeName)
    {
        var theme = Variant(themeName);
        await OnUi(() =>
        {
            var header = new PageHeader { Breadcrumb = "Passo 1 de 4", Title = "Novo projeto jogo Ajustes Capítulo", Margin = new Thickness(24) };

            ScreenRenderer.Render(header, theme, RenderOutput.PathFor(ScreenRenderer.FileName("base-cabecalho", theme)), 800, 120, window =>
            {
                var title = header.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
                var crumb = header.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Breadcrumb");

                var natural = FontsHeadlessTests.NaturalLineHeight(title);
                Assert.True(title.Bounds.Height >= natural - 0.01, $"o título tem {title.Bounds.Height} px e a fonte pede {natural}");
                Assert.True(title.Bounds.Bottom <= header.Bounds.Height + 0.01, "o título passa da base do cabeçalho");

                Assert.Equal("Noto Sans", crumb.FontFamily.Name.Split(',')[0].Split('#').Last().Trim());
                Assert.Equal(12, crumb.FontSize);
                Assert.Equal(Token(window, "Vnf.TextSecondaryColor"), SolidColor(crumb.Foreground));
            });
        });
    }

    private static async Task<MainWindowViewModel> Opened(TempFolder temp, Func<TextUnit, TextUnit>? first, Func<TextUnit, TextUnit>? second, ResolvedApiKey? key = null)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        var units = folder.LoadText("yst00001").ToList();
        units[0] = first?.Invoke(units[0]) ?? units[0];
        units[1] = second?.Invoke(units[1]) ?? units[1];
        folder.SaveText("yst00001", units);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(Now));
        var vm = new MainWindowViewModel(start, key);
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.Editor.Loading;
        return vm;
    }

    private static TextUnit WithError(TextUnit unit) => unit with { Source = @"「行こう」\p", Translation = "Vamos" };

    private static TextUnit WithWarning(TextUnit unit) => unit with { Translation = "duas  palavras" };

    /// <summary>A seção Conferir da segunda linha da Tradução, com o projeto no estado pedido. O quadro da #544 não desenha selo nem ponto nela.</summary>
    private async Task WithSection(ThemeVariant theme, Func<TextUnit, TextUnit>? first, Func<TextUnit, TextUnit>? second, string name, Action<Window, ListBoxItem> inspect)
    {
        using var temp = new TempFolder();
        var vm = await Opened(temp, first, second);
        await OnUi(() =>
        {
            var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
            try
            {
                window.Show();
                vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Dashboard);
                ScreenRenderer.Settle(window);
                var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is NavItemViewModel { Key: NavKey.Conference } && i.IsEffectivelyVisible);
                ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(name, theme)));
                inspect(window, item);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_secao_Conferir_nao_mostra_selo_nem_ponto_com_erro_aviso_ou_sem_problema(string themeName)
    {
        var theme = Variant(themeName);
        foreach (var (first, second, name) in new (Func<TextUnit, TextUnit>?, Func<TextUnit, TextUnit>?, string)[]
        {
            (WithError, WithWarning, "base-secao-erro"),
            (null, WithWarning, "base-secao-aviso"),
            (null, null, "base-secao-limpa"),
        })
        {
            await WithSection(theme, first, second, name, (_, item) =>
            {
                Assert.DoesNotContain(item.GetVisualDescendants().OfType<Border>(), b => b.CornerRadius == new CornerRadius(8) && b.IsEffectivelyVisible);
                Assert.DoesNotContain(item.GetVisualDescendants().OfType<Ellipse>(), e => e.IsEffectivelyVisible);
                Assert.Equal("Conferir", item.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible).Text);
            });
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Triangulo_da_barra_de_status_so_aparece_sem_chave_e_diz_qual_e_o_aviso(string themeName)
    {
        var theme = Variant(themeName);
        using var temp = new TempFolder();
        var without = await Opened(temp, null, null);
        using var temp2 = new TempFolder();
        var with = await Opened(temp2, null, null, new ResolvedApiKey("chave-de-teste", ApiKeySource.Environment));

        await OnUi(() =>
        {
            Show(without, theme, "base-status-sem-chave", NavKey.Editor, window =>
            {
                var panel = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "KeyStatus");
                Assert.Equal("Chave do Claude não configurada: a tradução com IA fica desligada", ToolTip.GetTip(panel));
                Assert.Single(panel.GetVisualDescendants().OfType<PathIcon>(), i => i.IsEffectivelyVisible);
                Assert.Equal(Token(window, "Vnf.SignalWarningFgColor"), SolidColor(panel.GetVisualDescendants().OfType<PathIcon>().Single(i => i.IsEffectivelyVisible).Foreground));
            });

            Show(with, theme, "base-status-com-chave", NavKey.Start, window =>
            {
                var panel = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "KeyStatus");
                var visible = panel.GetVisualDescendants().OfType<PathIcon>().Single(i => i.IsEffectivelyVisible);
                Assert.Equal(Token(window, "Vnf.StatusApprovedFgColor"), SolidColor(visible.Foreground));
            });
        });
    }

    private static void Show(MainWindowViewModel vm, ThemeVariant theme, string name, NavKey page, Action<Window> inspect)
    {
        vm.SelectedNav = vm.Nav.Single(n => n.Key == page);
        var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
        try
        {
            window.Show();
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName(name, theme)));
            inspect(window);
        }
        finally
        {
            window.Close();
        }
    }
}
