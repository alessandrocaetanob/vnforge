using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
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
/// O Painel da v3 montado de verdade com o projeto de demonstração, nos dois temas: a 1440×1000 para o PNG que vai ao lado do artboard (<c>VNFORGE_RENDER_DIR</c>)
/// e a 1440×900 para conferir que a tela inteira cabe sem rolar. A chave do Claude está configurada, a cotação foi informada e o limite é de R$ 300,
/// como no artboard; o resto sai da pasta (dois erros, 23 personagens sem gênero, 37 avisos).
/// </summary>
public sealed class DashboardRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Painel_mostra_o_mapa_o_precisa_de_voce_o_gasto_e_o_estado_do_jogo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await Abrir(theme, ScreenRenderer.Height, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);

            Assert.Equal(["Ler o texto", "Traduzir com a IA", "Revisar", "Conferir", "Testar no jogo"], view.GetVisualDescendants().OfType<PipelineStepItem>().Select(s => s.Title));
            // Quadro da #544: "38%" grande e uma linha, sem anel nem legenda.
            Assert.DoesNotContain(view.GetVisualDescendants(), v => v is ProgressRing);
            Assert.Equal(vm.Dashboard.MapPercentText, view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapPercent").Text);
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            Assert.Equal([.. DemoProject.ChapterTitles, ScriptGrouping.ExtraName], rows.Select(r => r.Title));
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, null], rows.Select(r => r.Number));
            Assert.True(rows[^1].IsExtra);

            var todos = view.GetVisualDescendants().OfType<TodoItem>().ToList();
            Assert.Equal(DashboardViewModel.MaxTodos, todos.Count);
            Assert.Equal(SeverityLevel.Error, todos[0].Severity);
            Assert.Equal("2 erros travam o teste no jogo", todos[0].Title);
            Assert.All(todos, t => Assert.False(string.IsNullOrEmpty(t.ActionText)));
            Assert.All(todos, t => Assert.True(string.IsNullOrEmpty(t.Message)));

            Assert.Equal("R$ 142,56 de R$ 300,00", vm.Dashboard.SpentText);
            var budget = Assert.Single(view.GetVisualDescendants().OfType<BudgetBar>());
            Assert.Equal(300, budget.Limit);
            Assert.DoesNotContain(view.GetVisualDescendants(), v => v is GameStateBanner);
            Assert.StartsWith("No seu ritmo", vm.Dashboard.PaceText, StringComparison.Ordinal);

            var texts = string.Join('\n', view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.DoesNotContain("YPF", texts, StringComparison.Ordinal);
            Assert.DoesNotContain("token", texts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claude-", texts, StringComparison.Ordinal);
            Assert.DoesNotContain(@"D:\", texts, StringComparison.Ordinal);
            Assert.DoesNotContain("US$", texts, StringComparison.Ordinal);

            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("painel-v3", theme)));
        });
    }

    /// <summary>
    /// O Painel é página de entrada, então leva o cabeçalho completo em qualquer altura de janela (opção C do #544). A 1440×900 o Painel passa 144 px
    /// da janela (extensão 746 de 602 de área); o teste prende esse corte para ele não crescer sem ninguém ver.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Painel_em_1440_por_900_rola_so_o_que_o_cabecalho_completo_toma(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await Abrir(theme, 900, (window, _) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>());

            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 144.5, $"o Painel passa mais de 144 px da janela: {scroll.Extent.Height} de {scroll.Viewport.Height}");
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("painel-v3-900", theme)));
        });
    }

    /// <summary>
    /// As medidas do Painel e da busca da barra de título tiradas dos pixels do quadro da #544 a 1440×1000 (conferência do #607): os três cartões na coluna da esquerda
    /// de 888 px contra 384 e 24 de vão, passos de 14/20 com quadradinho de 18 px, 38% e "O jogo inteiro" em 20/28, capítulos de 48 px com a barra de 160, "Precisa de você"
    /// em linhas de 50 px e o gasto com a barra de 6 px.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Painel_e_a_busca_da_barra_de_titulo_seguem_as_medidas_do_quadro(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await Abrir(theme, ScreenRenderer.Height, (window, _) =>
        {
            Color Token(string key) => window.TryFindResource(key, theme, out var found) && found is ISolidColorBrush brush ? brush.Color : throw new InvalidOperationException(key);
            static Point At(Visual control, Visual origin) => control.TranslatePoint(new Point(0, 0), origin)!.Value;
            static Border Card(Visual inside) => inside.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("panel"));

            // Barra de título: 28 px de altura, texto de 12, fundo bg-layer e contorno stroke-control.
            var search = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("palette-trigger"));
            Assert.Equal(28, search.Bounds.Height);
            Assert.Equal(420, search.Bounds.Width);
            var searchBox = search.GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Equal(Token("Vnf.BgLayer"), ((ISolidColorBrush)searchBox.Background!).Color);
            Assert.Equal(Token("Vnf.StrokeControl"), ((ISolidColorBrush)searchBox.BorderBrush!).Color);
            Assert.Equal(12, search.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Buscar jogos, personagens e comandos").FontSize);

            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var stepper = Assert.Single(view.GetVisualDescendants().OfType<PipelineStepper>());
            var side = view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "Side");
            var stepsCard = Card(stepper);
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            var chaptersCard = Card(rows[0]);
            var mapCard = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "MapPercent"));

            // Colunas: 888 e 384 com 24 de vão; os passos são o primeiro cartão da coluna da esquerda e a da direita começa na mesma altura.
            Assert.Equal(384, side.Bounds.Width);
            Assert.Equal(888, stepsCard.Bounds.Width);
            Assert.Equal(stepsCard.Bounds.Width, mapCard.Bounds.Width);
            Assert.Equal(stepsCard.Bounds.Width, chaptersCard.Bounds.Width);
            Assert.Equal(24, At(side, view).X - (At(stepsCard, view).X + stepsCard.Bounds.Width));
            Assert.Equal(At(stepsCard, view).Y, At(side, view).Y);
            Assert.Equal(16, At(mapCard, view).Y - (At(stepsCard, view).Y + stepsCard.Bounds.Height));

            // Passos: rótulo de 14/20 e subtítulo de 13/20; o concluído é o ícone de 24 e os outros o quadradinho de 18 px.
            var items = stepper.GetVisualDescendants().OfType<PipelineStepItem>().ToList();
            var title = items[1].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            var subtitle = items[1].GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Subtitle");
            Assert.Equal((14d, 20d), (title.FontSize, title.LineHeight));
            Assert.Equal((13d, 20d), (subtitle.FontSize, subtitle.LineHeight));
            Assert.Equal(new Size(18, 18), items[1].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Circle").Bounds.Size);
            Assert.Equal(new Size(24, 24), items[0].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Circle").Bounds.Size);
            Assert.Equal(3, items[1].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Underline").Bounds.Height);

            // 38% e "O jogo inteiro": o bloco começa 24 px dentro do cartão, o título é 20/28 e a linha de apoio 14.
            var percent = mapCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapPercent");
            var mapTitle = mapCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "O jogo inteiro");
            Assert.Equal(25, At(percent, mapCard).X);
            Assert.Equal((20d, 28d), (mapTitle.FontSize, mapTitle.LineHeight));
            Assert.Equal(14, mapCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "MapLine").FontSize);

            // Capítulos: título de 16/22, linhas de 48 px com a divisória, barra de 160 px.
            var chaptersTitle = chaptersCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Capítulos");
            Assert.Equal((16d, 22d), (chaptersTitle.FontSize, chaptersTitle.LineHeight));
            Assert.Equal(55, At(rows[0], chaptersCard).Y);
            Assert.Equal(48, At(rows[1], chaptersCard).Y - At(rows[0], chaptersCard).Y);
            var rowBorder = rows[0].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Root");
            Assert.Equal(new Thickness(0, 0, 0, 1), rowBorder.BorderThickness);
            Assert.Equal(Token("Vnf.StrokeSubtle"), ((ISolidColorBrush)rowBorder.BorderBrush!).Color);
            Assert.Equal(160, rows[0].GetVisualDescendants().OfType<ProgressBar>().Single().Bounds.Width);

            // Precisa de você: título de 16/22 sem divisória embaixo, linhas de 50 px, ícone de 24, título forte e ação de 14 semibold.
            var todos = view.GetVisualDescendants().OfType<TodoItem>().ToList();
            var needCard = Card(todos[0]);
            var needTitle = needCard.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Precisa de você");
            Assert.Equal((16d, 22d), (needTitle.FontSize, needTitle.LineHeight));
            Assert.Equal(55, At(todos[0], needCard).Y);
            Assert.Equal(50, At(todos[1], needCard).Y - At(todos[0], needCard).Y);
            Assert.Equal(new Size(24, 24), todos[0].GetVisualDescendants().OfType<PathIcon>().First().Bounds.Size);
            Assert.Equal(FontWeight.SemiBold, todos[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == todos[0].Title).FontWeight);
            var action = todos[0].GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal((14d, FontWeight.SemiBold), (action.FontSize, action.FontWeight));

            // Gasto com a IA: barra de 6 px, accent sobre o bg-inset.
            var budget = Assert.Single(view.GetVisualDescendants().OfType<BudgetBar>());
            Assert.Equal(6, budget.Bounds.Height);
            Assert.Equal(Token("Vnf.Accent"), ((ISolidColorBrush)budget.SpentBrush!).Color);
            Assert.True(budget.IsInset);
        });
    }

    public static TheoryData<string, int> ThemesAndWidths => new() { { "Dark", 1366 }, { "Light", 1366 }, { "Dark", 1440 }, { "Light", 1440 } };

    [Theory]
    [MemberData(nameof(ThemesAndWidths))]
    public async Task Passos_do_stepper_aparecem_inteiros_sem_reticencias(string themeName, int width)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await Abrir(theme, width == 1366 ? 768 : ScreenRenderer.Height, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var stepper = Assert.Single(view.GetVisualDescendants().OfType<PipelineStepper>());
            var steps = stepper.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).ToList();
            Assert.Equal(vm.Dashboard.Steps.Select(s => s.Subtitle), steps.Where(t => vm.Dashboard.Steps.Any(s => s.Subtitle == t.Text)).Select(t => t.Text));
            Assert.Equal(["48.211 linhas lidas", "62% traduzido", "18.802 revisadas", "2 erros travam", "Testado em 22/10"], vm.Dashboard.Steps.Select(s => s.Subtitle));
            Assert.All(steps, t => Assert.False(IsTrimmed(t), $"Stepper @{width}: \"{t.Text}\" aparado ({t.Bounds.Width:0.#}x{t.Bounds.Height:0.#})"));

            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, "o Painel passa da largura da janela");
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"painel-v4-{width}", theme)));
        }, width);
    }

    [Fact]
    public async Task A_coluna_do_andamento_dos_capitulos_usa_a_fonte_da_interface_com_algarismos_tabulares_alinhada_a_direita()
    {
        await Abrir(ThemeVariant.Dark, ScreenRenderer.Height, (window, _) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            var title = rows[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
            var percents = rows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3 && t.Text == r.PercentText)).ToList();

            Assert.All(percents, t =>
            {
                Assert.Equal(title.FontFamily, t.FontFamily);
                Assert.Equal(TextAlignment.Right, t.TextAlignment);
                Assert.NotNull(t.FontFeatures);
                Assert.NotEmpty(t.FontFeatures);
            });
            var right = percents.Select(t => Math.Round(t.TranslatePoint(new Point(t.Bounds.Width, 0), view)!.Value.X, 1)).Distinct().ToList();
            Assert.Single(right);
        });
    }

    /// <summary>
    /// O texto perde letras ou linhas: um bloco igual, medido na largura que o bloco recebeu (sem limite de linhas), pede mais largura ou mais altura do que o bloco tem.
    /// </summary>
    private static bool IsTrimmed(TextBlock block)
    {
        var natural = new TextBlock
        {
            Text = block.Text,
            FontFamily = block.FontFamily,
            FontSize = block.FontSize,
            FontWeight = block.FontWeight,
            FontStyle = block.FontStyle,
            LineHeight = block.LineHeight,
            TextWrapping = block.TextWrapping,
        };
        natural.Measure(block.TextWrapping == TextWrapping.Wrap ? new Size(block.Bounds.Width, double.PositiveInfinity) : Size.Infinity);
        return block.Bounds.Width + 0.5 < natural.DesiredSize.Width || block.Bounds.Height + 0.5 < natural.DesiredSize.Height;
    }

    [Fact]
    public async Task Renomear_troca_a_linha_do_capitulo_pelo_campo_de_nome_e_cancelar_a_devolve()
    {
        await Abrir(ThemeVariant.Dark, ScreenRenderer.Height, (window, vm) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var first = vm.Dashboard.Chapters[0];
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            Assert.All(rows, r => Assert.True(r.IsEffectivelyVisible));

            vm.Dashboard.BeginRenameChapterCommand.Execute(first);
            ScreenRenderer.Settle(window);

            var field = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
            Assert.Equal(first.Name, field.Text);
            Assert.False(rows[0].IsEffectivelyVisible);
            Assert.True(rows[1].IsEffectivelyVisible);

            vm.Dashboard.CancelRenameChapterCommand.Execute(first);
            ScreenRenderer.Settle(window);

            Assert.True(rows[0].IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
        });
    }

    private async Task Abrir(ThemeVariant theme, int height, Action<MainWindow, MainWindowViewModel> verificar, int width = ScreenRenderer.Width)
    {
        Prepare(demoFolder.Root);
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
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Dashboard);
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

    /// <summary>O ritmo de revisão (cinco dias de trabalho e algumas linhas hoje) e o jogo com a tradução de 22/10, para o Painel ter o que mostrar.</summary>
    private static void Prepare(string root)
    {
        var folder = ProjectFolder.Open(root);
        // As aprovações seguem o relógio fixo do Início: com o relógio real, o teste quebrava quando o dia passava de Now.
        var now = Now;
        var approvals = new List<ApprovalRecord>();
        for (var day = 4; day >= 1; day--)
        {
            for (var hour = 0; hour < 2; hour++)
            {
                approvals.AddRange(Enumerable.Range(0, 120).Select(i => new ApprovalRecord("yst00013", $"yst00013:{day:D2}{hour}{i:D4}", now.AddDays(-day).AddHours(-hour).AddSeconds(i * 30))));
            }
        }

        approvals.AddRange(Enumerable.Range(0, 30).Select(i => new ApprovalRecord("yst00013", $"yst00013:99{i:D4}", now.AddMinutes(-20).AddSeconds(i * 30))));
        SessionsFile.Save(folder, new SessionsFile(SessionsFile.CurrentVersion, approvals));

        var applied = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        GameStateFile.Save(folder, new GameStateFile(
            GameStateFile.CurrentVersion,
            new GameInGame(GameTestKind.Translation, applied, null, null, null),
            new GameLastTest(applied, GameTestKind.Translation, [])));
    }
}
