using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
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

    private async Task Abrir(ThemeVariant theme, int height, Action<MainWindow, MainWindowViewModel> verificar)
    {
        Prepare(demoFolder.Root);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demoFolder.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                vm.Dashboard.Context = () => new DashboardContext(new ExchangeRate(5.40m, new DateOnly(2026, 10, 22)), new BudgetLimits(300m, 50m), true);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = height, RequestedThemeVariant = theme, DataContext = vm };
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
        var now = DateTimeOffset.UtcNow;
        var approvals = new List<ApprovalRecord>();
        for (var day = 4; day >= 1; day--)
        {
            for (var hour = 0; hour < 2; hour++)
            {
                approvals.AddRange(Enumerable.Range(0, 120).Select(i => new ApprovalRecord("yst00013", $"yst00013:{day:D2}{hour}{i:D4}", now.AddDays(-day).AddHours(-hour).AddSeconds(i))));
            }
        }

        approvals.AddRange(Enumerable.Range(0, 30).Select(i => new ApprovalRecord("yst00013", $"yst00013:99{i:D4}", now.AddSeconds(-90 + i))));
        SessionsFile.Save(folder, new SessionsFile(SessionsFile.CurrentVersion, approvals));

        var applied = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        GameStateFile.Save(folder, new GameStateFile(
            GameStateFile.CurrentVersion,
            new GameInGame(GameTestKind.Translation, applied, null, null, null),
            new GameLastTest(applied, GameTestKind.Translation, [])));
    }
}
