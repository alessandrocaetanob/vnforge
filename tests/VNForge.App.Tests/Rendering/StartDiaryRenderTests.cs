using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
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
using VNForge.Core.Text;

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
                    Assert.Equal("dia que ainda não chegou", map.TipAt(CellCenter(column, 1)));
                    Assert.Equal(map.TipAt(CellCenter(0, 0)), map.TipAt(new Point(ReviewHeatmap.CellSize + 1, 2)));
                    Assert.Null(map.TipAt(new Point(-1, 2)));

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
                    Assert.Equal("nenhuma aprovação ainda", map.TipAt(new Point(2, 2)));
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

    [Fact]
    public async Task Passar_pelo_vao_entre_as_celulas_nao_fecha_o_tooltip_e_voltar_a_uma_celula_mostra_o_dia()
    {
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var sessions = new SessionsFile(SessionsFile.CurrentVersion, [.. Enumerable.Range(0, 5).Select(i => new ApprovalRecord("yst00013", $"yst00013:{i:D6}", Now.AddHours(-1).AddDays(-1).AddSeconds(i * 30)))]);
                var map = new ReviewHeatmap { Diary = ReviewDiary.Compute(sessions, Now, TimeZoneInfo.Utc), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(40) };
                var window = new Window { Width = 400, Height = 300, Content = map };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);
                    var origin = map.TranslatePoint(default, window)!.Value;
                    var column = ReviewDiary.Weeks - 2;
                    var onCell = origin + CellCenter(column, 6);
                    var inGap = origin + new Point(column * (ReviewHeatmap.CellSize + ReviewHeatmap.Gap) + ReviewHeatmap.CellSize + ReviewHeatmap.Gap / 2, CellCenter(column, 6).Y);
                    var other = origin + CellCenter(column, 5);

                    window.MouseMove(onCell);
                    await WaitFor(() => ToolTip.GetIsOpen(map));
                    Assert.Contains("sábado, 24/10: 5 linhas", TipText(map));

                    var tip = ToolTip.GetTip(map);
                    window.MouseMove(inGap);
                    ScreenRenderer.Settle(window);
                    Assert.NotNull(ToolTip.GetTip(map));
                    Assert.Same(tip, ToolTip.GetTip(map));
                    Assert.True(ToolTip.GetIsOpen(map), "passar pelo vão fechou o tooltip");

                    window.MouseMove(other);
                    ScreenRenderer.Settle(window);
                    Assert.True(ToolTip.GetIsOpen(map), "o tooltip não voltou numa célula depois do vão");
                    Assert.Contains("sexta, 23/10: nenhuma linha", TipText(map));
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
    public async Task O_mapa_de_calor_expoe_o_nome_ao_leitor_de_tela()
    {
        await fixture.Session.Dispatch<int>(
            () =>
            {
                var map = new ReviewHeatmap();
                AutomationProperties.SetName(map, "Mapa das aprovações por dia nas últimas 12 semanas, ainda sem aprovações.");
                var window = new Window { Width = 400, Height = 300, Content = map };
                try
                {
                    window.Show();
                    var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(map);

                    Assert.Equal("Mapa das aprovações por dia nas últimas 12 semanas, ainda sem aprovações.", peer.GetName());
                    Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Image, peer.GetAutomationControlType());
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Uma_falha_inesperada_no_resumo_so_fecha_a_janela_na_hora()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var clock = new ThrowingClock(Now);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), clock);
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, DataContext = vm };
                window.Show();
                await start.OpenAsync(root);
                await start.DetailLoading;
                clock.Throw = true;

                window.Close();

                Assert.False(window.IsVisible);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private sealed class ThrowingClock(DateTimeOffset now) : TimeProvider
    {
        public bool Throw { get; set; }

        public override DateTimeOffset GetUtcNow() => Throw ? throw new InvalidOperationException("falha inesperada do relógio") : now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static string? TipText(Control owner) => ToolTip.GetTip(owner) is TextBlock block ? block.Text : ToolTip.GetTip(owner)?.ToString();

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(25);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition(), "o tooltip não abriu");
    }

    [Fact]
    public async Task Uma_leitura_cancelada_pela_troca_de_projeto_nao_grava_o_diario()
    {
        using var temp = new TempFolder();
        var first = SampleProjects.Create(temp.SubFolder("a"), "Projeto A", TranslationStatus.Machine);
        var second = SampleProjects.Create(temp.SubFolder("b"), "Projeto B", TranslationStatus.Machine);
        SessionsFile.Save(ProjectFolder.Open(first), new SessionsFile(SessionsFile.CurrentVersion, [.. Enumerable.Range(0, 7).Select(i => new ApprovalRecord("yst00001", $"yst00001:{i:D6}", Now.AddHours(-1).AddSeconds(i * 30)))]));
        SessionsFile.Save(ProjectFolder.Open(second), new SessionsFile(SessionsFile.CurrentVersion, [.. Enumerable.Range(0, 3).Select(i => new ApprovalRecord("yst00001", $"yst00001:{i:D6}", Now.AddHours(-1).AddSeconds(i * 30)))]));
        // Na thread da tela, como no app: as leituras de dois projetos nunca mexem nas coleções ao mesmo tempo.
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var vm = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                await vm.OpenAsync(first);
                await vm.DetailLoading;
                await vm.OpenAsync(second);
                await vm.DetailLoading;

                vm.Selected = vm.Recents.Single(r => r.Name == "Projeto A");
                var cancelled = vm.DetailLoading;
                vm.Selected = vm.Recents.Single(r => r.Name == "Projeto B");
                await cancelled;
                await vm.DetailLoading;

                Assert.Equal(3, vm.Diary.Source.Today);
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
