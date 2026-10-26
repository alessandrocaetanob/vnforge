using Avalonia.Controls;
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
/// O Início da v3 montado de verdade nos dois temas a 1440×1000: com o projeto de demonstração (<c>inicio-v3-projetos</c>) e na primeira vez, sem projeto
/// (<c>inicio-v3-primeira-vez</c>). O texto visível não pode ter YPF, token, nome de modelo nem caminho de pasta; isso fica em "Detalhes técnicos", fechado.
/// </summary>
public sealed class StartV3RenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Inicio_com_projetos_mostra_a_lista_o_anel_o_proximo_passo_e_a_area_para_soltar(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        Prepare(demoFolder.Root);
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
                    Assert.Single(view.GetVisualDescendants().OfType<Seg3Bar>(), b => b.IsEffectivelyVisible);
                    Assert.Single(view.GetVisualDescendants().OfType<ProgressRing>(), r => r.IsEffectivelyVisible);
                    var texts = VisibleText(view);
                    Assert.Contains("Próximo passo", texts, StringComparison.Ordinal);
                    Assert.Contains("Escolher a pasta", VisibleButtons(view));
                    Assert.Contains(start.Detail.NextStepLabel, VisibleButtons(view));
                    Assert.True(start.OpenNextStepCommand.CanExecute(null));
                    Assert.StartsWith("No seu ritmo", start.Detail.PaceText, StringComparison.Ordinal);
                    Assert.Contains(start.Detail.PaceText, texts, StringComparison.Ordinal);
                    var technical = Assert.Single(view.GetVisualDescendants().OfType<TechnicalDetails>());
                    Assert.False(technical.IsExpanded);
                    AssertPlainLanguage(texts);

                    var status = StatusBarText(window);
                    Assert.Contains(vm.AppVersionText, status, StringComparison.Ordinal);
                    Assert.DoesNotContain("Acentuação", status, StringComparison.Ordinal);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("inicio-v3-projetos", theme)));
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
    public async Task Inicio_na_primeira_vez_mostra_os_quatro_passos_e_o_que_voce_precisa(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey(null, ApiKeySource.None));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<StartView>(), v => v.IsEffectivelyVisible);
                    var texts = VisibleText(view);
                    foreach (var step in StartViewModel.FirstSteps)
                    {
                        Assert.Contains(step.Title, texts, StringComparison.Ordinal);
                    }

                    Assert.Contains("O que você precisa", texts, StringComparison.Ordinal);
                    Assert.Contains("O jogo instalado", texts, StringComparison.Ordinal);
                    Assert.Contains("Uma chave do Claude", texts, StringComparison.Ordinal);
                    Assert.Contains("Espaço para a cópia de segurança", texts, StringComparison.Ordinal);
                    Assert.DoesNotContain(view.GetVisualDescendants().OfType<Seg3Bar>(), b => b.IsEffectivelyVisible);
                    AssertPlainLanguage(texts);

                    Assert.All(vm.Nav.Where(n => n.Key is not (NavKey.Start or NavKey.Settings)), n => Assert.False(n.IsEnabled));
                    var status = StatusBarText(window);
                    Assert.Contains(vm.AppVersionText, status, StringComparison.Ordinal);
                    Assert.Contains("Chave do Claude", status, StringComparison.Ordinal);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("inicio-v3-primeira-vez", theme)));
                }
                finally
                {
                    window.Close();
                }

                return Task.FromResult(0);
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>O ritmo de revisão (cinco dias de trabalho e algumas linhas hoje) e o jogo com a tradução de 22/10, como no Painel, para o cartão ter horas e o teste no jogo uma data.</summary>
    private static void Prepare(string root)
    {
        var folder = ProjectFolder.Open(root);
        var now = DateTimeOffset.UtcNow;
        var approvals = new List<ApprovalRecord>();
        for (var day = 4; day >= 1; day--)
        {
            for (var hour = 0; hour < 2; hour++)
            {
                approvals.AddRange(Enumerable.Range(0, 120).Select(i => new ApprovalRecord("yst00013", $"yst00013:{day:D2}{hour}{i:D4}", now.AddDays(-day).AddHours(-hour).AddSeconds(i * 30))));
            }
        }

        SessionsFile.Save(folder, new SessionsFile(SessionsFile.CurrentVersion, approvals));
        var applied = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        GameStateFile.Save(folder, new GameStateFile(
            GameStateFile.CurrentVersion,
            new GameInGame(GameTestKind.Translation, applied, null, null, null),
            new GameLastTest(applied, GameTestKind.Translation, [])));
    }

    private static string VisibleText(Control root) =>
        string.Join('\n', root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Inlines is { Count: > 0 } ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text));

    private static string[] VisibleButtons(Control root) =>
        [.. root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string ?? string.Empty)];

    private static string StatusBarText(MainWindow window) =>
        string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("caption")).Select(t => t.Text));

    private static void AssertPlainLanguage(string texts)
    {
        Assert.DoesNotContain("YPF", texts, StringComparison.Ordinal);
        Assert.DoesNotContain("YU-RIS", texts, StringComparison.Ordinal);
        Assert.DoesNotContain("token", texts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude-", texts, StringComparison.Ordinal);
        Assert.DoesNotContain(@":\", texts, StringComparison.Ordinal);
    }
}
