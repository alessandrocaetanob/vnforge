using Avalonia;
using Avalonia.Automation;
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
    public async Task Inicio_com_projetos_mostra_o_cartao_de_continuar_a_lista_compacta_e_um_so_botao_de_destaque(string themeName)
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
                    Assert.DoesNotContain("Próximo passo", texts, StringComparison.Ordinal);
                    Assert.Contains("Continuar de onde parou", texts, StringComparison.Ordinal);
                    Assert.Contains("Seus projetos", texts, StringComparison.Ordinal);
                    Assert.Contains("Escolher a pasta", VisibleButtons(view));
                    Assert.Equal("Continuar: revisar “A promessa”", start.Detail.ContinueLabel);
                    Assert.Contains(start.Detail.ContinueLabel, VisibleButtons(view));
                    Assert.Equal("A promessa · capítulo 5", start.Detail.NextStepScene);
                    Assert.Contains(start.Detail.NextStepScene, texts, StringComparison.Ordinal);
                    Assert.True(start.OpenNextStepCommand.CanExecute(null));
                    Assert.StartsWith("No seu ritmo", start.Detail.PaceText, StringComparison.Ordinal);
                    Assert.Contains(start.Detail.PaceText, texts, StringComparison.Ordinal);
                    Assert.Contains("Testado em 22/10", texts, StringComparison.Ordinal);
                    Assert.Contains("R$ 142,56 de R$ 300,00", texts, StringComparison.Ordinal);
                    var technical = Assert.Single(view.GetVisualDescendants().OfType<TechnicalDetails>(), t => t.IsEffectivelyVisible);
                    Assert.False(technical.IsExpanded);
                    AssertPlainLanguage(texts);

                    // Um único botão de destaque na tela, "Abrir projeto" uma vez só e "Novo projeto" comum como o vizinho.
                    var accent = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Classes.Contains("accent"));
                    Assert.Equal(start.Detail.ContinueLabel, accent.Content);
                    Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Abrir projeto");
                    Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Novo projeto");
                    Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Solte a pasta aqui para começar uma tradução nova.");

                    // O cartão tem a fala onde a pessoa vai continuar, na caixa do jogo pequena, e o botão principal fica na área visível.
                    var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>(), g => g.IsEffectivelyVisible);
                    Assert.Equal(GameTextboxSize.Large, box.Size);
                    Assert.NotNull(box.Layout);
                    Assert.Equal("Próxima fala a revisar", start.ContinueLineCaption);
                    Assert.Contains(start.ContinueLineCaption, texts, StringComparison.Ordinal);
                    var card = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Continuar de onde parou");
                    AssertInside(accent, card);
                    AssertInside(box, card);
                    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Assert.True(accent.TranslatePoint(new Avalonia.Point(0, accent.Bounds.Height), scroll)!.Value.Y <= scroll.Viewport.Height, "o botão principal passa da área visível");

                    // A área para soltar a pasta é a última linha da lista: abaixo das linhas, tracejada e mais baixa que uma linha de projeto.
                    var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>(), l => l.IsEffectivelyVisible);
                    var drop = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.Name == "ListDropArea");
                    Assert.True(drop.TranslatePoint(default, view)!.Value.Y >= list.TranslatePoint(new Avalonia.Point(0, list.Bounds.Height), view)!.Value.Y - 0.5);
                    Assert.True(drop.Bounds.Height < 56);
                    Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Abrir");

                    var status = StatusBarText(window);
                    Assert.Contains(vm.AppVersionText, status, StringComparison.Ordinal);
                    Assert.DoesNotContain(vm.EncodingText, status, StringComparison.Ordinal);
                    Assert.NotEmpty(vm.EncodingText);
                    Assert.False(vm.BoxLimitsText.Length > 0 && status.Contains(vm.BoxLimitsText, StringComparison.Ordinal));

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
    public async Task Inicio_com_projetos_a_1366_por_768_mantem_o_botao_principal_a_vista_e_o_texto_do_cartao_inteiro(string themeName)
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
                var window = new MainWindow { Width = 1366, Height = 768, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenAsync(demoFolder.Root);
                    await start.DetailLoading;
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<StartView>(), v => v.IsEffectivelyVisible);
                    var card = Assert.Single(view.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Continuar de onde parou");
                    var accent = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Classes.Contains("accent"));
                    var box = Assert.Single(view.GetVisualDescendants().OfType<GameTextbox>(), g => g.IsEffectivelyVisible);
                    AssertInside(accent, card);
                    AssertInside(box, card);
                    var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                    Assert.True(accent.TranslatePoint(new Avalonia.Point(0, accent.Bounds.Height), scroll)!.Value.Y <= scroll.Viewport.Height, "o botão principal passa da área visível");
                    Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, "o Início com um projeto passa da janela");
                    var clipped = ClippedTextHeadlessTests.FindClipped(card);
                    Assert.True(clipped.Count == 0, string.Join("; ", clipped));

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("inicio-v4-1366", theme)));
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
                    var primary = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Classes.Contains("accent"));
                    Assert.Same(start.NewProjectCommand, primary.Command);
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

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Inicio_com_projeto_que_sumiu_explica_sem_mostrar_o_caminho_fora_dos_detalhes_tecnicos(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, new ResolvedApiKey("sk-teste", ApiKeySource.Vault));
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenAsync(root);
                    await start.DetailLoading;
                    Directory.Delete(root, recursive: true);
                    await start.LoadAsync();
                    ScreenRenderer.Settle(window);

                    var view = Assert.Single(window.GetVisualDescendants().OfType<StartView>(), v => v.IsEffectivelyVisible);
                    var texts = VisibleText(view);
                    Assert.Contains("Projeto indisponível.", texts, StringComparison.Ordinal);
                    Assert.Contains("A pasta do projeto não foi encontrada.", texts, StringComparison.Ordinal);
                    AssertPlainLanguage(texts);
                    Assert.Single(view.GetVisualDescendants().OfType<TechnicalDetails>(), t => t.IsEffectivelyVisible);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("inicio-v3-indisponivel", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void AssertInside(Control child, Control parent)
    {
        var origin = child.TranslatePoint(default, parent)!.Value;
        Assert.True(origin.X >= -0.5 && origin.Y >= -0.5 && origin.X + child.Bounds.Width <= parent.Bounds.Width + 0.5 && origin.Y + child.Bounds.Height <= parent.Bounds.Height + 0.5,
            $"{child.GetType().Name} sai do cartão: {origin} {child.Bounds.Size} em {parent.Bounds.Size}");
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
