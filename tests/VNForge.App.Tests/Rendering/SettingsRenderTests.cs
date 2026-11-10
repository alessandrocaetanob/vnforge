using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza as abas dos Ajustes com o projeto de demonstração, a 1440×1000, nos dois temas, para pôr ao lado do artboard do canvas:
/// <c>ajustes-ia</c>, <c>ajustes-estilo</c>, <c>ajustes-formatacao</c> e <c>ajustes-acentos</c> (com cp1252 marcado, como no artboard).
/// </summary>
public sealed class SettingsRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly (int Tab, string Name)[] Tabs = [(0, "ajustes-v6-ia"), (1, "ajustes-v6-estilo"), (2, "ajustes-v6-formatacao"), (3, "ajustes-v6-acentos")];

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
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    await vm.Settings.EstimateLoading;

                    vm.Settings.ExchangeRateText = "5,40";
                    vm.Settings.SpendLimitText = "300";
                    vm.Settings.WarnAboveText = "25";

                    foreach (var (tab, name) in Tabs)
                    {
                        vm.Settings.SelectedTab = tab;
                        if (tab >= 2)
                        {
                            vm.Settings.EncodingStrategy = "cp1252";
                            vm.Settings.SelectedEllipsis = vm.Settings.EllipsisStyles.Single(s => s.Value == EllipsisStyle.Character);
                        }

                        ScreenRenderer.Settle(window);
                        if (name == "ajustes-v6-estilo")
                        {
                            ConferirEstilo(window);
                        }

                        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                        ScreenRenderer.Save(window, path);

                        using var png = new Bitmap(path);
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);

                        Assert.Contains(VNForge.Core.Vndb.VndbCredit.Text, window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
                        ConferirCabecalho(window);

                        if (tab == 2)
                        {
                            ConferirFormatacao(window);
                        }

                        if (tab == 0)
                        {
                            ConferirAbaIaEGasto(window, theme);
                        }

                        if (tab == 3)
                        {
                            ConferirAcentos(window, theme);
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

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Sem_limite_de_gasto_a_barra_some_e_a_cotacao_de_exemplo_aparece_uma_vez(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vault = new MemoryApiKeyStore();
                var settings = new SettingsViewModel(vault, new AppSettingsFile(config.SubFolder("ajustes")), _ => null, _ => throw new InvalidOperationException("a renderização não chama a API"));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey(null, ApiKeySource.None), settings: settings);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
                    await vm.Settings.EstimateLoading;
                    vm.Settings.SelectedTab = 0;
                    ScreenRenderer.Settle(window);
                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("ajustes-v6-ia-sem-chave", theme)));

                    Assert.False(vm.Settings.HasLimit);
                    Assert.DoesNotContain(window.GetVisualDescendants().OfType<BudgetBar>(), b => b.IsEffectivelyVisible);

                    var texts = VisibleTexts(window);
                    Assert.Single(texts, t => t.Contains("cotação de exemplo", StringComparison.Ordinal));
                    Assert.Contains("Qualidade padrão · valores com cotação de exemplo", texts);
                    Assert.Contains(texts, t => t.EndsWith("gastos neste projeto", StringComparison.Ordinal));
                    Assert.DoesNotContain(texts, t => t.Contains("vnforge.json", StringComparison.Ordinal));
                    Assert.Contains("Valem só para este projeto. A chave do Claude fica guardada no Windows.", texts);
                    Assert.Equal("Salvo", vm.StatusNote);

                    var adicionar = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("accent") && b.IsEffectivelyVisible);
                    Assert.Equal("Adicionar chave", string.Join(" ", adicionar.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text)));
                    Assert.Equal(32, adicionar.Bounds.Height);
                    Assert.False(Botao(window, "Testar conexão").IsEffectivelyEnabled);
                    Assert.All(window.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("field")), b => Assert.Equal(32, b.Bounds.Height));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static List<string> VisibleTexts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!)];

    /// <summary>
    /// Na aba Estilo: os cinco cartões existem com um marcado por grupo, e a caixa do exemplo traduzido tem a moldura de destaque e
    /// texto dentro. Conta tinta em retângulos conhecidos, não faixas de texto, porque a fonte da CI não é a do Windows.
    /// </summary>
    private static void ConferirEstilo(Window window)
    {
        var cards = window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("option")).ToList();
        Assert.Equal(5, cards.Count);
        Assert.Equal([false, false, true, true, false], cards.Select(c => c.IsChecked == true));

        Border Caixa(string nome) => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == nome);
        var source = Caixa("StyleExampleSource");
        var target = Caixa("StyleExampleTarget");
        Assert.True(target.Bounds.Width > 200 && target.Bounds.Height > 30, "A caixa do exemplo traduzido ficou sem tamanho.");
        Assert.InRange(Math.Abs(source.Bounds.Width - target.Bounds.Width), 0, 1);

        Assert.True(target.TryFindResource("Vnf.Accent", target.ActualThemeVariant, out var found));
        var accent = Assert.IsType<SolidColorBrush>(found, exactMatch: false).Color;

        // A caixa fica na borda de baixo da janela de 1000: com a fonte da CI ela passa dela. Rola até a caixa para capturar e devolve a página
        // à posição de antes, para o PNG salvo em seguida continuar sendo o da aba aberta no topo.
        var scroller = target.FindAncestorOfType<ScrollViewer>() ?? throw new InvalidOperationException("A aba Estilo não está dentro de um ScrollViewer.");
        var offsetBefore = scroller.Offset;
        PixelProbe probe;
        try
        {
            target.BringIntoView();
            ScreenRenderer.Settle(window);
            probe = PixelProbe.Capture(window, target);
        }
        finally
        {
            scroller.Offset = offsetBefore;
            ScreenRenderer.Settle(window);
        }

        Assert.True(probe.CountInRow(0, accent, 12, 8, probe.Width - 8) >= probe.Width - 18, "A moldura de destaque do exemplo não foi desenhada.");

        var inside = new Rect(12, 8, probe.Width - 24, probe.Height - 16);
        var background = probe.At(6, probe.Height - 6);
        var area = (int)inside.Width * (int)inside.Height;
        var ink = area - probe.CountInArea(inside, background, 10);
        Assert.True(ink > 40, $"O texto do exemplo traduzido não deixou tinta na caixa ({ink} pixels).");
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
        Assert.All(cards, c => Assert.Equal(Orientation.Horizontal, c.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_PriceRow").Orientation));
        Assert.All(cards, c => Assert.Equal(new CornerRadius(16), c.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Card").CornerRadius));

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
        Assert.DoesNotContain(VisibleTexts(window), t => t.Contains("cotação de exemplo", StringComparison.Ordinal));
        Assert.True(bar.Bounds.Width > 200 && bar.Bounds.Height >= BudgetBar.BarHeight);
        Assert.True(window.TryFindResource("Vnf.TextPrimary", theme, out var textBrush));
        var mark = ((ISolidColorBrush)textBrush!).Color;
        var markArea = new Rect(0, 0, bar.Bounds.Width, bar.Bounds.Height);
        Assert.True(PixelProbe.Capture(window, bar).CountInArea(markArea, mark, 24) >= BudgetBar.BarHeight, "marca do limite na barra");
    }

    /// <summary>
    /// Na aba Acentos: três cartões lado a lado dentro da janela, o do ajuste no jogo marcado com a borda de destaque e o das fontes
    /// modificadas desligado, cada um com a frase na caixa do jogo; nenhum nome técnico fora de "Detalhes técnicos", que vem fechado.
    /// </summary>
    private static void ConferirAcentos(Window window, ThemeVariant theme)
    {
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        var cards = tabs.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("option") && r.IsEffectivelyVisible)
            .OrderBy(r => r.TranslatePoint(default, window)!.Value.X).ToList();
        Assert.Equal(3, cards.Count);
        Assert.Equal(["Sem acentos", "Com acentos, fonte modificada", "Com acentos, ajuste no jogo"], cards.Select(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)));
        Assert.Equal([false, false, true], cards.Select(c => c.IsChecked == true));
        Assert.Equal([true, false, true], cards.Select(c => c.IsEnabled));
        Assert.All(cards, c => Assert.True(c.Bounds.Width > 200 && c.Bounds.Height > 100, $"{c.Bounds}"));
        var lefts = cards.Select(c => c.TranslatePoint(default, window)!.Value).ToList();
        Assert.True(lefts[0].X + cards[0].Bounds.Width <= lefts[1].X + 1 && lefts[1].X + cards[1].Bounds.Width <= lefts[2].X + 1, "cartões sobrepostos");
        Assert.True(lefts[2].X + cards[2].Bounds.Width <= window.Bounds.Width);

        var boxes = tabs.GetVisualDescendants().OfType<GameTextbox>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal(3, boxes.Count);
        Assert.All(boxes, b => Assert.True(b.Bounds.Width > 100 && b.Bounds.Height > 30, $"{b.Bounds}"));
        Assert.All(boxes, b => Assert.Equal(90, Assert.IsType<Border>(b.Parent).Bounds.Height, 1));
        Assert.Single(cards.Select(c => c.Bounds.Height).Distinct());
        Assert.All(cards, c => Assert.Equal(new CornerRadius(16), c.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Card").CornerRadius));
        Assert.All(cards, c => Assert.Single(c.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("radio-mark")));

        Assert.True(window.TryFindResource("Vnf.Accent", theme, out var found));
        var accent = ((ISolidColorBrush)found!).Color;
        var borderTop = new Rect(8, 0, cards[2].Bounds.Width - 16, 3);
        Assert.True(PixelProbe.Capture(window, cards[2]).CountInArea(borderTop, accent, 24) > cards[2].Bounds.Width / 2, "borda do accent no cartão marcado");
        Assert.Equal(0, PixelProbe.Capture(window, cards[0]).CountInArea(borderTop, accent, 24));

        var details = tabs.GetVisualDescendants().OfType<TechnicalDetails>().Single();
        Assert.False(details.IsExpanded);
        var texts = string.Join(" ", tabs.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        foreach (var technical in new[] { "fold", "remap", "cp1252", "sjis", "CP1252", "YPF", "Shift-JIS", "token", "build", "byte" })
        {
            Assert.DoesNotContain(technical, texts);
        }
    }

    private static Button Botao(Window window, string texto) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == texto);

    /// <summary>
    /// O cabeçalho do quadro v6: a etiqueta do projeto de 24 de altura ao lado do título e as abas de 41 de altura com 32 entre uma e outra.
    /// </summary>
    private static void ConferirCabecalho(Window window)
    {
        var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("project-tag") && b.IsEffectivelyVisible);
        Assert.Equal(24, chip.Bounds.Height);
        Assert.StartsWith("Projeto: ", chip.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal(new CornerRadius(999), chip.CornerRadius);

        var tabs = window.GetVisualDescendants().OfType<TabItem>().OrderBy(i => i.TranslatePoint(default, window)!.Value.X).ToList();
        Assert.Equal(["IA e gasto", "Estilo", "Formatação", "Acentos", "Dicionários", "Aparência"], tabs.Select(i => i.Header as string));
        Assert.All(tabs, i => Assert.Equal(41, i.Bounds.Height));
        for (var n = 1; n < tabs.Count; n++)
        {
            var gap = tabs[n].TranslatePoint(default, window)!.Value.X - (tabs[n - 1].TranslatePoint(default, window)!.Value.X + tabs[n - 1].Bounds.Width);
            Assert.InRange(gap, 31.5, 32.5);
        }
    }

    /// <summary>
    /// A aba Formatação com o aviso de estratégia: linhas de regra de 63 de passo, o segmentado em cápsula com o segmento marcado num cartão,
    /// a faixa de aviso com raio 16 e o botão de um clique, e os campos de 32 de altura sem setas.
    /// </summary>
    private static void ConferirFormatacao(Window window)
    {
        var rules = window.GetVisualDescendants().OfType<ToggleRule>().Where(r => r.IsEffectivelyVisible).ToList();
        Assert.Equal(7, rules.Count);
        Assert.All(rules.Take(6).Zip(rules.Skip(1).Take(5)), pair => Assert.Equal(63, pair.Second.Bounds.Top - pair.First.Bounds.Top));

        var pills = window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Classes.Contains("pill") && l.IsEffectivelyVisible).ToList();
        Assert.Equal(2, pills.Count);
        Assert.All(pills, l => Assert.Equal(new CornerRadius(999), l.CornerRadius));
        Assert.All(pills.SelectMany(l => l.GetVisualDescendants().OfType<ListBoxItem>()), i => Assert.Equal(28, i.Bounds.Height));
        var ellipsis = pills.Last();
        Assert.Equal(1, ellipsis.SelectedIndex);
        Assert.True(((ListBoxItem)ellipsis.ContainerFromIndex(1)!).IsSelected);

        var banner = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("banner") && b.Classes.Contains("warning") && b.IsEffectivelyVisible);
        Assert.Equal(new CornerRadius(16), banner.CornerRadius);
        Assert.Contains("cp1252 não grava as reticências", string.Join(" ", banner.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text)));
        Assert.Single(banner.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("accent"));

        var numbers = window.GetVisualDescendants().OfType<NumericUpDown>().Where(n => n.IsEffectivelyVisible).ToList();
        Assert.Equal(2, numbers.Count);
        Assert.All(numbers, n => Assert.Equal(32, n.Bounds.Height));
        Assert.All(numbers, n => Assert.False(n.ShowButtonSpinner));

        var panels = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("panel") && b.IsEffectivelyVisible).ToList();
        Assert.Equal(2, panels.Count);
        Assert.All(panels, p => Assert.Equal(new CornerRadius(16), p.CornerRadius));
        Assert.Equal(330, panels.Min(p => p.Bounds.Width), 1);
    }
}
