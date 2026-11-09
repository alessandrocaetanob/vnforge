using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.AI.Batches;
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
                lines: [("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Approved), ("yst00002", TranslationStatus.Untranslated)]);
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

    private static async Task SendAsync(MainWindowViewModel vm, Window window, string script)
    {
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == script);
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
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
    public async Task Renderiza_a_lista_de_tarefas_com_um_lote_que_chegou_com_erro_e_outro_em_andamento(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        await OnUi(
            async (vm, window, client) =>
            {
                client.OutcomeOf = item => MessageBatchOutcome.Errored;
                await SendAsync(vm, window, "yst00001");
                var first = vm.TaskService.Tasks.Single();
                client.Finish("msgbatch_fake0001");
                first.CheckNowCommand!.Execute(null);
                await vm.NightBatchWatching;
                ScreenRenderer.Settle(window);
                client.OutcomeOf = _ => MessageBatchOutcome.Succeeded;
                await SendAsync(vm, window, "yst00002");
                await WaitForAsync(window, () => vm.TaskService.Tasks.Count == 2 && vm.TaskService.Tasks[1].Detail.Contains("verificado às", StringComparison.Ordinal));

                var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Flyout is not null && b.Content is string text && text.StartsWith("Tarefas (", StringComparison.Ordinal));
                // O Flyout fica fora do quadro capturado da janela; o conteúdo da lista é desenhado sozinho, com o mesmo contexto de dados.
                var content = (Control)((Flyout)button.Flyout!).Content!;
                content.DataContext = vm.Tasks;
                var path = RenderOutput.PathFor(ScreenRenderer.FileName("lista-de-tarefas-com-lote", theme));
                ScreenRenderer.Render(
                    new Border { Padding = new Avalonia.Thickness(16), Child = content },
                    theme,
                    path,
                    480,
                    520,
                    inspect: shown =>
                    {
                        var visuals = shown.GetVisualDescendants().ToList();
                        var texts = visuals.OfType<TextBlock>().Select(t => t.Text).ToList();
                        var buttons = visuals.OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
                        Assert.Contains("Tradução noturna · Esta cena", texts);
                        Assert.Contains(texts, t => t is not null && t.StartsWith("Lote enviado às ", StringComparison.Ordinal));
                        Assert.True(texts.Any(t => t is not null && t.StartsWith("Chegou às ", StringComparison.Ordinal) && t.EndsWith("2 voltaram com erro", StringComparison.Ordinal)), string.Join(" | ", texts));
                        Assert.Contains("Verificar agora", buttons);
                        Assert.Contains("Cancelar lote", buttons);
                        Assert.Contains("Ver as 2 linhas", buttons);
                    });
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
                Assert.Contains("Desfazer o lote \"Esta cena\"?", texts);
                Assert.Contains("Voltam ao estado anterior", texts);
                Assert.Contains("Mantidas porque você editou", texts);
                var buttons = OverlayLayer.GetOverlayLayer(window)!.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
                Assert.Contains("Manter", buttons);
                Assert.Contains("Desfazer 1 linha", buttons);

                Save(window, "desfazer-o-lote", theme);

                vm.UndoBatch.KeepCommand.Execute(null);
                await asking;
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
