using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O shell da #544 montado de verdade com o projeto de demonstração, nos dois temas: o trilho com a Biblioteca marcada, o cabeçalho do jogo, as abas
/// Ficha, Tradução e Jogar e a segunda linha da Tradução, a 1440×1000 (os PNGs <c>shell-*</c>) e em 1366×768, onde o cabeçalho cabe numa linha só.
/// </summary>
public sealed class ShellRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Painel_dentro_do_jogo_tem_o_trilho_o_cabecalho_as_abas_e_a_segunda_linha(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Dashboard, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            Assert.False(header.IsCompact);

            var rail = window.GetVisualDescendants().OfType<ListBox>().Where(l => l.Classes.Contains("navrail") && l.IsEffectivelyVisible).ToList();
            Assert.Equal(["Biblioteca", "Ajustes"], rail.SelectMany(l => l.GetVisualDescendants().OfType<TextBlock>()).Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text));
            var library = Assert.Single(rail[0].GetVisualDescendants().OfType<ListBoxItem>());
            Assert.True(library.IsSelected);
            var label = library.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Biblioteca");
            Assert.Equal(11, label.FontSize);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, label.FontWeight);
            var railTop = library.TranslatePoint(default, rail[0])!.Value.Y + rail[0].TranslatePoint(default, window)!.Value.Y;
            Assert.Equal(16, railTop - 40, 1);

            Assert.Equal("Biblioteca › " + vm.ProjectTitle, vm.Breadcrumb);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.Breadcrumb && t.IsEffectivelyVisible);

            var tabs = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("gametabs"));
            Assert.Equal(["Ficha", "Tradução", "Jogar"], tabs.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal(GameTab.Translation, Assert.IsType<GameTabItem>(tabs.SelectedItem).Tab);

            var sections = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("sections"));
            Assert.True(sections.IsEffectivelyVisible);
            Assert.Equal(["Painel", "Editor", "Revisão", "Conferir", "Testar", "Glossário", "Mais"], sections.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible).Select(i => ((NavItemViewModel)i.DataContext!).Label));
            Assert.Equal("Painel", ((NavItemViewModel)sections.SelectedItem!).Label);

            var texts = header.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains(vm.ProjectTitle, texts);
            Assert.Contains("Yu-RIS", texts);
            Assert.Contains(vm.GameProgressLabel, texts);
            Assert.Contains(vm.Dashboard.NextStepLabel, texts);
            Assert.DoesNotContain("YPF", string.Join('\n', texts), StringComparison.Ordinal);

            var cover = Assert.Single(header.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Width == 56 && b.Height == 75);
            Assert.Equal(new CornerRadius(16), cover.CornerRadius);
            Assert.True(cover.BoxShadow.Count > 0, "a capa tem sombra");

            var pill = Assert.Single(header.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Classes.Contains("game-pill"));
            Assert.DoesNotContain("done", pill.Classes);
            var fill = Assert.Single(header.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Parent is Border { Height: 5 } && b.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left);
            Assert.InRange(vm.GameProgressPercent, 1, 99);
            var expected = GameHeaderView.CoverWidth * vm.GameProgressPercent / 100;
            Assert.InRange(fill.Bounds.Width, expected - 1, expected + 1);

            var dashboard = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            Assert.Empty(dashboard.GetVisualDescendants().OfType<PageHeader>());
            var scroll = Assert.Single(dashboard.GetVisualDescendants().OfType<ScrollViewer>());
            // O Painel não tem mais a faixa Visão geral | Provas (as Provas são um item do "Mais"); a folga de 42 px ficou da medida antiga e o Painel não rola mais que isso em 1440x1000.
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 42.5, $"o Painel rola mais que os 42 px da faixa de abas em 1440x1000: {scroll.Extent.Height} de {scroll.Viewport.Height}");
            Assert.True(header.Bounds.Height < 260, $"o cabeçalho e a segunda linha ocupam {header.Bounds.Height:0} px");

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-painel", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Ficha_e_Jogar_ficam_dentro_do_jogo_sem_a_segunda_linha(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Sheet, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            var tabs = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("gametabs"));
            Assert.Equal(GameTab.Sheet, Assert.IsType<GameTabItem>(tabs.SelectedItem).Tab);
            Assert.False(Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Classes.Contains("sections")).IsEffectivelyVisible);

            // O botão principal do quadro na Ficha é "Jogar", desabilitado até a #549, e o próximo passo do Painel não aparece.
            var play = Assert.Single(header.GetVisualDescendants().OfType<Button>(), b => b.Name == "PlayButton" && b.IsEffectivelyVisible);
            Assert.False(play.IsEnabled);
            Assert.Contains(play.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Jogar");
            Assert.DoesNotContain(header.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.Dashboard.OpenNextStepCommand && b.IsEffectivelyVisible);

            // O projeto de amostra não tem o vínculo com o VNDB: a Ficha diz isso em vez de mostrar uma ficha vazia.
            var page = Assert.Single(window.GetVisualDescendants().OfType<GameSheetView>(), v => v.IsEffectivelyVisible);
            var empty = Assert.Single(page.GetVisualDescendants().OfType<EmptyState>());
            Assert.Equal(vm.Sheet.EmptyMessage, empty.Message);

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-ficha", theme)));

            vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Play);
            ScreenRenderer.Settle(window);
            Assert.Single(window.GetVisualDescendants().OfType<GameTabPlaceholderView>(), v => v.IsEffectivelyVisible);
            Assert.Equal(GameTab.Play, Assert.IsType<GameTabItem>(tabs.SelectedItem).Tab);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-jogar", theme)));
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Em_1366_por_768_o_cabecalho_cabe_numa_linha_e_o_Editor_ganha_a_altura(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, 1366, 768, NavKey.Editor, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            Assert.True(header.IsCompact);
            Assert.DoesNotContain(header.GetVisualDescendants().OfType<Button>(), b => (b.Command == vm.Dashboard.OpenNextStepCommand || b.Name == "PlayButton") && b.IsEffectivelyVisible);
            Assert.Single(header.GetVisualDescendants().OfType<Grid>(), g => g.Name == "Compact" && g.IsEffectivelyVisible);
            Assert.Single(header.GetVisualDescendants().OfType<Grid>(), g => g.Name == "Regular" && !g.IsEffectivelyVisible);
            Assert.True(header.Bounds.Height < 170, $"o cabeçalho compacto ocupa {header.Bounds.Height:0} px");

            var title = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Classes.Contains("title-md"));
            Assert.False(string.IsNullOrEmpty(title.Text));
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-editor-1366", theme)));
        });
    }

    /// <summary>O que o shell tira da página: a altura do cabeçalho do jogo e a de onde a página começa (com a segunda linha), completo e compacto. O completo vale em todas as páginas menos nas quatro de trabalho, em qualquer altura de janela; o compacto, no Editor, no Conferir, no Testar e no Glossário. Mudar estes números muda o espaço de todas as telas do jogo.</summary>
    [Theory]
    [InlineData(1000, false, 179, 229, NavKey.Dashboard)]
    [InlineData(1000, true, 80, 80, NavKey.Editor)]
    [InlineData(768, false, 179, 229, NavKey.Dashboard)]
    [InlineData(768, true, 80, 80, NavKey.Glossary)]
    public async Task O_cabecalho_do_jogo_ocupa_a_altura_combinada(int height, bool compact, int expectedHeader, int expectedWithSections, NavKey page)
    {
        await Abrir(ThemeVariant.Dark, height == 768 ? 1366 : ScreenRenderer.Width, height, page, (window, _) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            var border = header.GetVisualDescendants().OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 0, 0, 1));
            Assert.Equal(compact, header.IsCompact);
            var page = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "PageArea");
            Assert.Equal(expectedHeader, (int)Math.Round(border.Bounds.Height));
            Assert.Equal(expectedWithSections, (int)Math.Round(page.TranslatePoint(default, header)!.Value.Y));
        });
    }

    /// <summary>
    /// A decisão do #544 (opção C): o cabeçalho completo na entrada do jogo (Ficha, Painel, Jogar, Lançar, Bíblia e Imagens) e o compacto, de 80 px, no Editor, no Conferir, no Testar e no Glossário. A troca
    /// acontece ao entrar e ao sair, uma tela de trabalho depois da outra e voltando a cada uma das telas de entrada.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_cabecalho_completo_da_entrada_vira_compacto_nas_telas_de_trabalho_e_volta_ao_sair(string themeName)
    {
        NavKey[] work = [NavKey.Editor, NavKey.Conference, NavKey.Build, NavKey.Glossary];
        NavKey[] entry = [NavKey.Sheet, NavKey.Dashboard, NavKey.Play, NavKey.Release, NavKey.Bible, NavKey.Images];
        await Abrir(ThemeOf(themeName), ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Sheet, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            var border = header.GetVisualDescendants().OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 0, 0, 1));
            Assert.False(header.IsCompact, "o jogo abre com o cabeçalho completo");
            var fullHeight = (int)Math.Round(border.Bounds.Height);
            Assert.True(fullHeight > 80, $"o completo tem {fullHeight} px");

            foreach (var screen in work)
            {
                foreach (var from in entry)
                {
                    Go(vm, window, from);
                    Assert.False(header.IsCompact, $"{from} tem o cabeçalho completo");
                    Assert.Equal(fullHeight, (int)Math.Round(border.Bounds.Height));

                    Go(vm, window, screen);
                    Assert.True(header.IsCompact, $"{screen} tem o cabeçalho compacto");
                    Assert.Equal(80, (int)Math.Round(border.Bounds.Height));
                    Assert.Single(header.GetVisualDescendants().OfType<Grid>(), g => g.Name == "Compact" && g.IsEffectivelyVisible);
                    Assert.Single(header.GetVisualDescendants().OfType<Grid>(), g => g.Name == "Regular" && !g.IsEffectivelyVisible);
                }
            }

            // Entre duas telas de trabalho o compacto continua; sair pelas abas do jogo também devolve o completo.
            Go(vm, window, NavKey.Editor);
            Go(vm, window, NavKey.Glossary);
            Assert.True(header.IsCompact);
            vm.SelectedGameTab = vm.GameTabs.Single(t => t.Tab == GameTab.Sheet);
            ScreenRenderer.Settle(window);
            Assert.False(header.IsCompact);
        });
    }

    /// <summary>Cada página do jogo no shell, a 1440×1000 nos dois temas (os PNGs <c>shell-pagina-*</c>), para comparar com o quadro do #544.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Cada_pagina_do_jogo_aparece_no_shell_a_1440_por_1000(string themeName)
    {
        (NavKey Key, string Name, bool Compact)[] pages =
        [
            (NavKey.Sheet, "ficha", false), (NavKey.Dashboard, "painel", false), (NavKey.Editor, "editor", true), (NavKey.Conference, "conferir", true),
            (NavKey.Build, "testar", true), (NavKey.Release, "lancar", false), (NavKey.Glossary, "glossario", true), (NavKey.Bible, "biblia", false),
            (NavKey.Images, "imagens", false), (NavKey.Play, "jogar", false),
        ];
        var theme = ThemeOf(themeName);
        await Abrir(theme, ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Sheet, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            foreach (var (key, name, compact) in pages)
            {
                Go(vm, window, key);
                Assert.Equal(compact, header.IsCompact);
                Assert.False(header.MoreMenu?.IsOpen ?? false, $"o menu do Mais abriu sozinho em {name}");
                if (key is not (NavKey.Sheet or NavKey.Play))
                {
                    // Uma margem esquerda só: o conteúdo de cada página começa onde o cabeçalho do jogo começa (x=112 a 1440 px, como no quadro: o contorno de 1 px do conteúdo fica por cima, sem empurrar nada).
                    var pageArea = Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "PageArea");
                    var pageLeft = pageArea.TranslatePoint(default, window)!.Value.X;
                    var left = ContentLeft(window, pageArea, vm.CurrentPage);
                    Assert.True(left >= pageLeft + 31.5 && left <= pageLeft + 32.5, $"{name}: o conteúdo começa em x={left}, o cabeçalho em x={pageLeft + 32}");
                }

                ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-pagina-" + name, theme)));
            }
        });
    }

    /// <summary>
    /// No compacto a segunda linha sobe para a linha das abas só se couber ao lado da última aba; o limite vem da largura medida, sem número fixo. Senão desce
    /// para a própria linha e o cabeçalho cresce para cerca de 120 px. Em nenhuma largura de 960 a 1440 ela cobre uma aba, o título ou a pílula.
    /// </summary>
    [Theory]
    [InlineData("Dark", 960)]
    [InlineData("Light", 960)]
    [InlineData("Dark", 1125)]
    [InlineData("Light", 1125)]
    [InlineData("Dark", 1366)]
    [InlineData("Light", 1366)]
    [InlineData("Dark", 1440)]
    public async Task A_segunda_linha_do_compacto_nunca_cobre_as_abas_e_desce_quando_nao_cabe(string themeName, int width)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, width, 800, NavKey.Editor, (window, _) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            Assert.True(header.IsCompact);
            var tabs = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "GameTabs");
            var sections = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "Sections");
            var line = header.GetVisualDescendants().OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 0, 0, 1));
            Rect Box(Control control) => new(control.TranslatePoint(default, header)!.Value, control.Bounds.Size);

            var natural = sections.ItemsPanelRoot!.Children.Where(c => c.IsVisible).Sum(c => c.DesiredSize.Width) + (sections.ItemsPanelRoot.Children.Count(c => c.IsVisible) - 1) * 4 + sections.Padding.Left + sections.Padding.Right;
            var fits = Box(tabs).Right + GameHeaderView.SectionsGap + natural + 32 <= header.Bounds.Width;
            Assert.Equal(!fits, header.Classes.Contains("sections-below"));

            var box = Box(sections);
            var covered = header.GetVisualDescendants().Where(v => v is TextBlock or Button && v.IsEffectivelyVisible && !sections.IsVisualAncestorOf(v) && !ReferenceEquals(v, sections))
                .OfType<Control>().Where(c => Box(c).Intersects(box)).Select(c => $"{c.GetType().Name} {Box(c)}").ToList();
            Assert.True(covered.Count == 0, $"a segunda linha ({box}) cobre {string.Join("; ", covered)} a {width} px");
            Assert.False(Box(tabs).Intersects(box), $"a segunda linha {box} cobre as abas {Box(tabs)} a {width} px");

            if (fits)
            {
                Assert.True(box.Left >= Box(tabs).Right + GameHeaderView.SectionsGap - 0.5);
                Assert.Equal(80, (int)Math.Round(line.Bounds.Height));
            }
            else
            {
                Assert.True(box.Top >= line.Bounds.Bottom - 0.5, $"a segunda linha começa em y={box.Top}, abaixo do filete em y={line.Bounds.Bottom}");
                Assert.InRange(header.Bounds.Height, 110, 130);
            }

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"shell-compacto-{width}", theme)));
        });
    }

    /// <summary>O "Mais" é o sétimo item da segunda linha e abre o menu com Lançar, Bíblia e Imagens; escolher uma leva à página e deixa o "Mais" marcado.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_Mais_abre_o_menu_com_as_tres_paginas_e_fica_marcado_na_pagina_escolhida(string themeName)
    {
        var theme = ThemeOf(themeName);
        await Abrir(theme, ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Dashboard, (window, vm) =>
        {
            var header = Assert.Single(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            var sections = Assert.Single(header.GetVisualDescendants().OfType<ListBox>(), l => l.Name == "Sections");
            var items = sections.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible).ToList();
            Assert.Equal(7, items.Count);
            Assert.Equal("Mais", ((NavItemViewModel)items[^1].DataContext!).Label);
            Assert.False(items[^1].IsSelected);

            header.ShowMoreMenu();
            ScreenRenderer.Settle(window);
            var menu = Assert.IsType<Avalonia.Controls.MenuFlyout>(header.MoreMenu);
            Assert.True(menu.IsOpen);
            Assert.Equal(vm.MoreSections.Count(s => s.IsVisible), menu.Items.Count);
            Assert.Equal(vm.MoreSections.Where(s => s.IsVisible).Select(s => s.Label), menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
            Assert.Equal("Provas", menu.Items.OfType<MenuItem>().Last().Header);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("shell-menu-mais", theme)));

            foreach (var (label, page) in new (string, Func<object>)[] { ("Lançar", () => vm.Release), ("Bíblia", () => vm.Bible) })
            {
                var entry = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == label);
                entry.Command!.Execute(entry.CommandParameter);
                ScreenRenderer.Settle(window);
                Assert.Same(page(), vm.CurrentPage);
                Assert.True(items[^1].IsSelected, $"o Mais fica marcado em {label}");
            }
        });
    }

    /// <summary>
    /// O x do que a página desenha mais à esquerda: o menor entre os cartões (bordas de 200 px ou mais que não ocupam a largura toda) e os textos soltos. O que
    /// é a própria página inteira, como a rolagem, não conta. Imagens sem inventário só tem uma frase centrada, e aí vale a pilha da página.
    /// </summary>
    private static double ContentLeft(MainWindow window, Border pageArea, object page)
    {
        var area = new Rect(pageArea.TranslatePoint(default, window)!.Value, pageArea.Bounds.Size);
        var view = pageArea.GetVisualDescendants().OfType<UserControl>().First(u => ReferenceEquals(u.DataContext, page) && u.IsEffectivelyVisible);
        var lefts = view.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width >= 100 && c.Bounds.Height >= 12 && c.Bounds.Width < area.Width - 40 && (c is TextBlock { Text.Length: > 0 } || c is Border { Background: not null } || (page is ImagesViewModel { IsEmpty: true } && c is StackPanel)))
            .Select(c => c.TranslatePoint(default, window)!.Value.X)
            .Where(x => x >= area.Left + 8)
            .ToList();
        return lefts.Count == 0 ? double.NaN : lefts.Min();
    }

    private static void Go(MainWindowViewModel vm, MainWindow window, NavKey key)
    {
        // A Conferência não deixa a página enquanto lê a pasta; a troca espera a leitura acabar.
        for (var i = 0; i < 500 && vm.Conference.IsBusy; i++)
        {
            ScreenRenderer.Settle(window);
            Thread.Sleep(20);
        }

        vm.SelectedNav = vm.Nav.Single(n => n.Key == key);
        ScreenRenderer.Settle(window);
        Assert.Equal(key, vm.SelectedNav!.Key);
    }

    [Fact]
    public async Task Fora_do_jogo_a_Biblioteca_e_Ajustes_nao_mostram_cabecalho_nem_abas()
    {
        await Abrir(ThemeVariant.Dark, ScreenRenderer.Width, ScreenRenderer.Height, NavKey.Start, (window, vm) =>
        {
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            Assert.Equal("Biblioteca", vm.Breadcrumb);

            vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Settings);
            ScreenRenderer.Settle(window);

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<GameHeaderView>(), v => v.IsEffectivelyVisible);
            Assert.Equal("Biblioteca › " + vm.ProjectTitle, vm.Breadcrumb);
        });
    }

    private async Task Abrir(ThemeVariant theme, int width, int height, NavKey page, Action<MainWindow, MainWindowViewModel> verificar)
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                vm.Dashboard.Context = () => new DashboardContext(new ExchangeRate(5.40m, new DateOnly(2026, 10, 22)), new BudgetLimits(300m, 50m), true);
                var window = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == page);
                    await vm.Dashboard.ReloadAsync();
                    ScreenRenderer.Settle(window);

                    verificar(window, vm);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}
