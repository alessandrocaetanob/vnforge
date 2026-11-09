using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.AI.Batches;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A lista de tarefas com dois lotes noturnos (um que chegou com linhas com erro e um em andamento) e o diálogo "Desfazer o lote?", a 1440×1000, nos dois temas,
/// para pôr ao lado do quadro <c>#issue-531</c>.
/// </summary>
public sealed class NightBatchRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private Task<int> OnUi(Func<MainWindowViewModel, Window, FakeMessageBatchClient, Task> test, ThemeVariant theme) => fixture.Session.Dispatch<int>(
        async () =>
        {
            using var temp = new TempFolder();
            var client = new FakeMessageBatchClient();
            var root = StartFixtures.Create(
                temp.SubFolder("vn"),
                "Minha VN",
                // 60 linhas pendentes em yst00001: o lote as parte em dois pedidos (50 no máximo cada), e um deles pode voltar com erro sem derrubar o outro.
                lines: [.. Enumerable.Repeat(("yst00001", TranslationStatus.Untranslated), 60), ("yst00001", TranslationStatus.Approved), ("yst00002", TranslationStatus.Untranslated)]);
            var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
            var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
            var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), new BackgroundTaskService(a => Dispatcher.UIThread.Post(a)), settings)
            {
                MessageBatchClientFactory = () => client,
                NightBatchPollInterval = TimeSpan.FromHours(1),
            };
            var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
            try
            {
                window.Show();
                await start.OpenAsync(root);
                start.OpenSelectedInEditorCommand.Execute(null);
                await vm.Editor.Loading;
                vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
                await test(vm, window, client);
            }
            finally
            {
                window.Close();
            }

            return 0;
        },
        TestContext.Current.CancellationToken);

    private static async Task SendAsync(MainWindowViewModel vm, Window window, string script, TranslationScope? scope = null)
    {
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == script);
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        if (scope is { } chosen)
        {
            vm.Translate.SelectedScope = vm.Translate.Scopes.Single(s => s.Scope == chosen);
        }

        vm.Translate.SelectedWhen = vm.Translate.Whens.Single(w => w.Overnight);
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;
        ScreenRenderer.Settle(window);
    }

    private static void Save(Window window, string name, ThemeVariant theme)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_lista_de_tarefas_com_tres_tarefas_e_o_popup_na_posicao_do_quadro(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window, client) =>
            {
                // Esta cena: o lote chega com o primeiro pedido gravado (Desfazer lote) e o segundo com erro (Ver as linhas).
                client.OutcomeOf = item => item.CustomId == "r00002" ? MessageBatchOutcome.Errored : MessageBatchOutcome.Succeeded;
                await SendAsync(vm, window, "yst00001");
                var first = vm.TaskService.Tasks.Single();
                client.Finish("msgbatch_fake0001");
                first.CheckNowCommand!.Execute(null);
                await vm.NightBatchWatching;
                ScreenRenderer.Settle(window);

                // Uma tarefa avulsa de ontem, com ponto de restauração: "Usar em todos".
                vm.TaskService.AddFinished("Usar em todos · \"Hehe.\"", Now.AddDays(-1), "14 linhas", new BatchRestore(vm.CurrentProject!.Root, "ponto-de-ontem", 14));

                // O lote que está em andamento.
                client.OutcomeOf = _ => MessageBatchOutcome.Succeeded;
                await SendAsync(vm, window, "yst00002");
                await WaitForAsync(window, () => vm.TaskService.Tasks.Count == 3 && vm.TaskService.Tasks.Any(t => t.IsRunning && t.Detail.Contains("verificado às", StringComparison.Ordinal)));

                var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Flyout is not null && Avalonia.Automation.AutomationProperties.GetName(b) == "Lista de tarefas");
                Assert.Equal("Tradução noturna: 1 em andamento", vm.Tasks.NightBatchText);
                var flyout = (Flyout)button.Flyout!;
                flyout.ShowAt(button);
                ScreenRenderer.Settle(window);

                var content = (Control)flyout.Content!;
                // O popup abre na camada de sobreposição da janela (ou numa raiz própria): a caixa dele é a do FlyoutPresenter, com a borda de 1 px.
                var presenter = content.FindAncestorOfType<FlyoutPresenter>() ?? throw new InvalidOperationException("O conteúdo da lista não está num FlyoutPresenter.");
                var bounds = PopupCapture.BoundsIn(window, presenter);
                // A lista abre sobre o Editor, no canto inferior esquerdo, com 600 px: 96 px da borda esquerda e 8 px acima da barra de status.
                Assert.True(
                    Math.Abs(bounds.X - TaskBar.FlyoutLeft) < 1 && Math.Abs(bounds.Width - 600) < 1 && Math.Abs(bounds.Bottom - (ScreenRenderer.Height - 28 - 8)) < 1.5,
                    $"o popup ficou em {bounds}; o quadro o põe em x 96, com 600 px de largura e a base em y {ScreenRenderer.Height - 28 - 8}");

                PopupCapture.Save(window, presenter, RenderOutput.PathFor(ScreenRenderer.FileName("lista-de-tarefas-com-lote", theme)));

                var visuals = content.GetVisualDescendants().ToList();
                var texts = visuals.OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                var buttons = visuals.OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string ?? b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text).ToList();
                Assert.Contains("Tarefas", texts);
                Assert.Contains("Fechar o app não cancela o lote", texts);
                Assert.DoesNotContain("Limpar concluídas", texts);
                Assert.Contains("Tradução noturna · Esta cena", texts);
                Assert.Contains(texts, t => t is not null && t.StartsWith("Lote enviado às ", StringComparison.Ordinal));
                Assert.True(texts.Any(t => t is not null && t.StartsWith("Chegou às ", StringComparison.Ordinal) && t.EndsWith(" voltaram com erro", StringComparison.Ordinal)), string.Join(" | ", texts));
                Assert.Contains("Verificar agora", buttons);
                Assert.Contains("Cancelar lote", buttons);
                Assert.Contains(buttons, b => b is not null && b.StartsWith("Ver as ", StringComparison.Ordinal));
                Assert.Contains("Desfazer lote", buttons);
                Assert.Contains("Desfazer", buttons);
                Assert.Single(visuals.OfType<ProgressBar>(), b => b.IsEffectivelyVisible);

                flyout.Hide();
            },
            theme);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_dialogo_Desfazer_o_lote(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window, client) =>
            {
                await SendAsync(vm, window, "yst00001");
                var task = vm.TaskService.Tasks.Single();
                client.Finish("msgbatch_fake0001");
                task.CheckNowCommand!.Execute(null);
                await vm.NightBatchWatching;
                ScreenRenderer.Settle(window);
                new LineEditor(ProjectFolder.Open(vm.CurrentProject!.Root), TimeProvider.System).SetTranslation("yst00001", "yst00001:000001", "à mão");

                var asking = vm.Tasks.Entries.Single().UndoCommand.ExecuteAsync(null);
                await WaitForAsync(window, () => vm.UndoBatch.IsOpen);
                ScreenRenderer.Settle(window);

                var texts = OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Desfazer o lote desta cena?", texts);
                Assert.Contains("Voltam ao estado anterior", texts);
                Assert.Contains("Mantidas porque você editou", texts);
                var buttons = OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
                Assert.Contains("Manter", buttons);
                Assert.Contains("Desfazer 59 linhas", buttons);

                Save(window, "desfazer-o-lote", theme);

                vm.UndoBatch.KeepCommand.Execute(null);
                await asking;
            },
            theme);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_dialogo_Desfazer_o_lote_com_os_dados_do_quadro(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window, _) =>
            {
                // O lote da Sala de aula do quadro: 1.204 linhas, 17 editadas depois; o botão devolve as 1.187 que sobram.
                var asking = vm.UndoBatch.AskAsync("Sala de aula", new DateTimeOffset(2026, 11, 25, 5, 12, 0, TimeSpan.Zero), new RestoreUndoResult(1187, 17, 0));
                await WaitForAsync(window, () => vm.UndoBatch.IsOpen);
                ScreenRenderer.Settle(window);

                var overlay = OverlayLayer.GetOverlayLayer(window)!;
                var texts = overlay.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Desfazer o lote da Sala de aula?", texts);
                Assert.Contains("1.187 linhas", texts);
                Assert.Contains("17 linhas", texts);
                Assert.Contains(overlay.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Desfazer 1.187 linhas");

                Save(window, "desfazer-o-lote-do-quadro", theme);

                vm.UndoBatch.KeepCommand.Execute(null);
                Assert.False(await asking);
            },
            theme);
    }

    private static async Task WaitForAsync(Window window, Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "A condição não chegou a tempo.");
            ScreenRenderer.Settle(window);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
