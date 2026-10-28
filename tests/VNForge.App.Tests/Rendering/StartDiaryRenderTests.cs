using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Tests.Demo;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// O Diário de tradução do Início montado de verdade nos dois temas a 1440×1000: o mapa de calor das últimas 12 semanas, os dias seguidos e o que foi aprovado
/// ontem (<c>inicio-v5-diario</c>), e o resumo da sessão que a janela mostra ao fechar sem travar o fechamento.
/// </summary>
public sealed class StartDiaryRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    // Domingo, 25/10/2026: a última coluna do mapa tem só o dia de hoje.
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Inicio_mostra_o_mapa_de_calor_os_dias_seguidos_e_o_que_foi_aprovado_ontem_sem_rolar_a_1000_de_altura(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Seed(demoFolder.Root);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                start.Detail.Context = () => new DashboardContext(new ExchangeRate(5.40m, new DateOnly(2026, 10, 22)), new BudgetLimits(300m, 50m), true);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenAsync(demoFolder.Root);
                    await start.DetailLoading;
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<StartView>(), v => v.IsEffectivelyVisible);
                    var panel = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Diário de tradução");
                    var map = Assert.Single(panel.GetVisualDescendants().OfType<ReviewHeatmap>());
                    Assert.Equal(ReviewHeatmap.MapWidth, map.Bounds.Width, 0.5);
                    Assert.Equal(ReviewHeatmap.MapHeight, map.Bounds.Height, 0.5);
                    Assert.Equal(start.Diary.MapDescription, AutomationProperties.GetName(map));

                    var texts = VisibleText(panel);
                    Assert.Contains("Diário de tradução", texts, StringComparison.Ordinal);
                    Assert.Contains("Últimas 12 semanas", texts, StringComparison.Ordinal);
                    Assert.Contains("um quadrado por dia", texts, StringComparison.Ordinal);
                    Assert.Contains("3", texts.Split('\n'));
                    Assert.Contains("dias seguidos", texts, StringComparison.Ordinal);
                    Assert.Contains("Ontem você aprovou 120 linhas.", texts, StringComparison.Ordinal);
                    Assert.Contains("Hoje: 24 linhas.", texts, StringComparison.Ordinal);
                    Assert.DoesNotContain('—', texts);
                    Assert.DoesNotContain('–', texts);

                    // O dia de hoje (domingo, última coluna) e o de ontem (sábado) têm a tooltip do dia e a quantidade.
                    var column = ReviewDiary.Weeks - 1;
                    Assert.Equal("domingo, 25/10: 24 linhas", map.TipAt(CellCenter(column, 0)));
                    Assert.Equal("sábado, 24/10: 120 linhas", map.TipAt(CellCenter(column - 1, 6)));
                    Assert.Equal("sexta, 23/10: 80 linhas", map.TipAt(CellCenter(column - 1, 5)));
                    Assert.Equal("quinta, 22/10: nenhuma linha", map.TipAt(CellCenter(column - 1, 4)));
                    Assert.Null(map.TipAt(CellCenter(column, 1)));
                    Assert.Null(map.TipAt(new Point(ReviewHeatmap.CellSize + 1, 0)));

                    // O dia mais cheio tem a cor de pronto do tema e um dia sem aprovação, a cor de pendente.
                    var approved = Resolve(map, "Vnf.SegApproved", theme);
                    var pending = Resolve(map, "Vnf.SegPending", theme);
                    var probe = PixelProbe.Capture(window, map);
                    var full = CellCenter(column - 1, 6);
                    var emptyCell = CellCenter(column - 1, 4);
                    Assert.True(probe.Near((int)full.X, (int)full.Y, approved, 6), $"célula cheia: {probe.At((int)full.X, (int)full.Y)} esperada {approved}");
                    Assert.True(probe.Near((int)emptyCell.X, (int)emptyCell.Y, pending, 6), $"célula vazia: {probe.At((int)emptyCell.X, (int)emptyCell.Y)} esperada {pending}");

                    // O Início com um projeto cabe na janela de 1000 de altura, com o diário abaixo da lista de projetos e antes dos detalhes técnicos.
                    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, $"o Início passa da janela: {scroll.Extent.Height} em {scroll.Viewport.Height}");
                    var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>(), l => l.IsEffectivelyVisible);
                    var technical = Assert.Single(view.GetVisualDescendants().OfType<TechnicalDetails>(), t => t.IsEffectivelyVisible);
                    Assert.True(panel.TranslatePoint(default, view)!.Value.Y >= list.TranslatePoint(new Point(0, list.Bounds.Height), view)!.Value.Y);
                    Assert.True(technical.TranslatePoint(default, view)!.Value.Y >= panel.TranslatePoint(new Point(0, panel.Bounds.Height), view)!.Value.Y - 0.5);
                    var clipped = ClippedTextHeadlessTests.FindClipped(panel);
                    Assert.True(clipped.Count == 0, string.Join("; ", clipped));

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("inicio-v5-diario", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Projeto_sem_aprovacoes_mostra_o_mapa_vazio_e_o_convite()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = ThemeVariant.Dark, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenAsync(root);
                    await start.DetailLoading;
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<StartView>(), v => v.IsEffectivelyVisible);
                    var panel = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Diário de tradução");
                    var map = Assert.Single(panel.GetVisualDescendants().OfType<ReviewHeatmap>());
                    Assert.Equal(ReviewHeatmap.MapWidth, map.Bounds.Width, 0.5);
                    Assert.Null(map.TipAt(new Point(2, 2)));
                    var texts = VisibleText(panel);
                    Assert.Contains("Aprove algumas linhas e o diário começa a se preencher.", texts, StringComparison.Ordinal);
                    Assert.DoesNotContain("nas últimas 12 semanas.", texts, StringComparison.Ordinal);
                    Assert.Contains("Ontem você não aprovou linhas.", texts, StringComparison.Ordinal);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Ao_fechar_a_janela_mostra_o_resumo_da_sessao_e_fecha_de_vez_no_segundo_pedido()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var clock = new FakeTimeProvider(Now);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), clock);
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenAsync(root);
                    await start.DetailLoading;
                    var session = Enumerable.Range(0, 20).Select(i => new ApprovalRecord("yst00013", $"yst00013:{i:D6}", Now.AddMinutes(5).AddSeconds(i * 30)));
                    SessionsFile.Save(ProjectFolder.Open(root), new SessionsFile(SessionsFile.CurrentVersion, [.. session]));
                    clock.Advance(TimeSpan.FromMinutes(40));

                    window.Close();

                    Assert.True(window.IsVisible, "o resumo adia o fechamento uma vez");
                    var toast = Assert.Single(vm.Toasts.Toasts, t => t.Key == "session-summary");
                    Assert.Equal("Nesta sessão: 20 linhas aprovadas em cerca de 10 min.", toast.Message);

                    window.Close();

                    Assert.False(window.IsVisible, "o segundo pedido de fechar não espera pelo resumo");
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Sem_aprovacoes_na_sessao_a_janela_fecha_na_hora()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                window.Show();
                await start.OpenAsync(root);
                await start.DetailLoading;

                window.Close();

                Assert.False(window.IsVisible);
                Assert.DoesNotContain(vm.Toasts.Toasts, t => t.Key == "session-summary");
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Doze semanas de trabalho com a sequência de hoje: 24 linhas hoje, 120 ontem, 80 anteontem, nenhuma antes de ontem-ontem e dias soltos no resto.</summary>
    private static void Seed(string root)
    {
        var approvals = new List<ApprovalRecord>();
        for (var daysAgo = 0; daysAgo <= 80; daysAgo++)
        {
            var lines = daysAgo switch
            {
                0 => 24,
                1 => 120,
                2 => 80,
                3 => 0,
                4 => 40,
                _ => daysAgo % 6 == 0 ? 0 : 10 + daysAgo * 13 % 50,
            };
            var at = Now.AddDays(-daysAgo).AddHours(-1);
            approvals.AddRange(Enumerable.Range(0, lines).Select(i => new ApprovalRecord("yst00013", $"yst00013:{daysAgo:D3}{i:D4}", at.AddSeconds(i * 20))));
        }

        SessionsFile.Save(ProjectFolder.Open(root), new SessionsFile(SessionsFile.CurrentVersion, approvals));
    }

    private static Point CellCenter(int column, int row) => new(
        column * (ReviewHeatmap.CellSize + ReviewHeatmap.Gap) + ReviewHeatmap.CellSize / 2,
        row * (ReviewHeatmap.CellSize + ReviewHeatmap.Gap) + ReviewHeatmap.CellSize / 2);

    private static Color Resolve(Control control, string key, ThemeVariant theme) =>
        control.TryFindResource(key, theme, out var found) && found is ISolidColorBrush brush ? brush.Color : throw new InvalidOperationException($"Sem o recurso {key}.");

    private static string VisibleText(Control root) =>
        string.Join('\n', root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
}
