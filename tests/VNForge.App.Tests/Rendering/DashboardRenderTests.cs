using Avalonia;
using Avalonia.Controls;
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
            Assert.Single(view.GetVisualDescendants().OfType<ProgressRing>());
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            Assert.Equal([.. DemoProject.ChapterTitles, ScriptGrouping.ExtraName], rows.Select(r => r.Title));
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, null], rows.Select(r => r.Number));
            Assert.True(rows[^1].IsExtra);

            var todos = view.GetVisualDescendants().OfType<TodoItem>().ToList();
            Assert.Equal(DashboardViewModel.MaxTodos, todos.Count);
            Assert.Equal(SeverityLevel.Error, todos[0].Severity);
            Assert.Equal("2 erros travam o teste no jogo", todos[0].Title);
            Assert.All(todos, t => Assert.False(string.IsNullOrEmpty(t.ActionText)));

            Assert.Equal("R$ 142,56 de R$ 300,00", vm.Dashboard.SpentText);
            var budget = Assert.Single(view.GetVisualDescendants().OfType<BudgetBar>());
            Assert.Equal(300, budget.Limit);
            Assert.Equal(GameState.Translated, Assert.Single(view.GetVisualDescendants().OfType<GameStateBanner>()).State);
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

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Painel_cabe_em_1440_por_900_sem_rolar(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await Abrir(theme, 900, (window, _) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>());

            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, $"o Painel passa da janela: {scroll.Extent.Height} de {scroll.Viewport.Height}");
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("painel-v3-900", theme)));
        });
    }

    public static TheoryData<string, int> ThemesAndWidths => new() { { "Dark", 1366 }, { "Light", 1366 }, { "Dark", 1440 }, { "Light", 1440 } };

    [Theory]
    [MemberData(nameof(ThemesAndWidths))]
    public async Task Passos_do_stepper_e_notas_dos_capitulos_aparecem_inteiros_sem_reticencias(string themeName, int width)
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

            var notes = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Name == "PART_Note" && !string.IsNullOrEmpty(t.Text)).ToList();
            Assert.NotEmpty(notes);
            Assert.All(notes, t =>
            {
                Assert.Equal(TextWrapping.Wrap, t.TextWrapping);
                Assert.Equal(2, t.MaxLines);
                Assert.False(IsTrimmed(t), $"Nota @{width}: \"{t.Text}\" aparada ({t.Bounds.Width:0.#}x{t.Bounds.Height:0.#})");
            });

            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, "o Painel passa da largura da janela");
            ScreenRenderer.Settle(window);
            ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName($"painel-v4-{width}", theme)));
        }, width);
    }

    [Fact]
    public async Task A_coluna_de_linhas_dos_capitulos_usa_a_fonte_da_interface_com_algarismos_tabulares_alinhada_a_direita()
    {
        await Abrir(ThemeVariant.Dark, ScreenRenderer.Height, (window, _) =>
        {
            var view = Assert.Single(window.GetVisualDescendants().OfType<DashboardView>(), v => v.IsEffectivelyVisible);
            var rows = view.GetVisualDescendants().OfType<ChapterRow>().ToList();
            var title = rows[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Title");
            var lines = rows.Select(r => r.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == r.LinesText)).ToList();

            Assert.All(lines, t =>
            {
                Assert.Equal(title.FontFamily, t.FontFamily);
                Assert.Equal(TextAlignment.Right, t.TextAlignment);
                Assert.NotNull(t.FontFeatures);
                Assert.NotEmpty(t.FontFeatures);
            });
            var right = lines.Select(t => Math.Round(t.TranslatePoint(new Point(t.Bounds.Width, 0), view)!.Value.X, 1)).Distinct().ToList();
            Assert.Single(right);
        });
    }

    [Fact]
    public async Task Nota_comprida_do_capitulo_quebra_em_duas_linhas_dentro_da_linha_de_44_px()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var row = new ChapterRow { Title = "Manhã de chuva", Number = 1, Done = 10, Review = 5, Total = 20, Note = "Você está aqui · 1 erro em Manhã de chuva" };
                var window = new Window { Width = 760, Height = 120, Content = row };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);

                    var note = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Note");
                    Assert.True(note.Bounds.Height > note.LineHeight * 1.5, $"a nota não quebrou: {note.Bounds.Height}");
                    Assert.True(note.Bounds.Height <= (note.LineHeight * 2) + 0.5);
                    Assert.False(IsTrimmed(note));
                    Assert.True(note.Bounds.Height <= row.Bounds.Height);
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
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
