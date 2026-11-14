using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Durante a noite": a janela manda o lote pela Message Batches API (cliente falso), a lista de tarefas o acompanha e o resultado é gravado quando o lote termina.</summary>
[Trait("Risco", "R2")]
[Trait("Risco", "R5")]
[Trait("Risco", "R6")]
public sealed class MainWindowNightBatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();
    private readonly FakeMessageBatchClient client = new();

    public void Dispose() => temp.Dispose();

    private StartViewModel? start;

    private async Task<(MainWindowViewModel Vm, string Root)> OpenAsync(bool withClient = true, Func<string, Task>? beforeOpen = null, Func<IMessageBatchClient>? factory = null)
    {
        var root = StartFixtures.Create(
            temp.SubFolder("vn"),
            "Minha VN",
            lines: [("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Approved), ("yst00002", TranslationStatus.Untranslated)]);
        if (beforeOpen is not null)
        {
            await beforeOpen(root);
        }

        start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), null, settings)
        {
            MessageBatchClientFactory = withClient ? factory ?? (() => client) : null,
            NightBatchPollInterval = TimeSpan.FromHours(1),
        };
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return (vm, root);
    }

    private static async Task SendOvernightAsync(MainWindowViewModel vm)
    {
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        vm.Translate.SelectedWhen = vm.Translate.Whens.Single(w => w.Overnight);
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;
    }

    private static async Task FinishAsync(MainWindowViewModel vm, FakeMessageBatchClient client, BackgroundTaskItem task)
    {
        client.Finish("msgbatch_fake0001");
        task.CheckNowCommand!.Execute(null);
        await vm.NightBatchWatching;
        await task.Completion;
    }

    [Fact]
    public async Task Ao_abrir_o_projeto_o_app_retoma_o_lote_que_ficou_na_pasta()
    {
        var (vm, root) = await OpenAsync(beforeOpen: async root =>
        {
            var service = new NightBatchService(client, ProjectFolder.Open(root));
            await service.SubmitAsync(
                new NightBatchOrder("O capítulo", ClaudeModels.Opus, ClaudeEffort.Medium, 20, new ExchangeRate(5m), key => key == "yst00001"),
                TestContext.Current.CancellationToken);
        });

        var task = Assert.Single(vm.TaskService.Tasks);
        Assert.Equal("Tradução noturna · O capítulo", task.Title);
        Assert.True(task.IsRunning);

        await FinishAsync(vm, client, task);

        Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
        Assert.Equal(1, client.Creates);
    }

    [Fact]
    public async Task Cancelar_lote_pede_o_cancelamento_a_API_e_o_que_ja_voltou_e_gravado_quando_ela_encerra()
    {
        var (vm, root) = await OpenAsync();
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.AfterGet = () => polled.TrySetResult();
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var watching = vm.NightBatchWatching;

        task.CancelCommand.Execute(null);
        await watching;

        Assert.Equal(1, client.Cancels);
        Assert.Equal(BackgroundTaskState.Cancelled, task.State);
        Assert.Equal(NightBatchPhase.Canceling, Assert.Single(new NightBatchStore(ProjectFolder.Open(root)).Open()).Phase);
        Assert.Equal(2, vm.TaskService.Tasks.Count);
    }

    [Fact]
    public async Task Lote_que_chega_com_outro_projeto_aberto_grava_no_dele_e_nao_mexe_no_projeto_atual()
    {
        var (vm, root) = await OpenAsync();
        client.OutcomeOf = _ => MessageBatchOutcome.Errored;
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();
        var other = StartFixtures.Create(temp.SubFolder("vn2"), "Outra VN", lines: [("yst00001", TranslationStatus.Untranslated)]);
        await start!.OpenAsync(other);
        Assert.Equal(Path.GetFullPath(other), Path.GetFullPath(vm.CurrentProject!.Root));

        await FinishAsync(vm, client, task);
        task.ShowLinesCommand!.Execute(null);

        Assert.Equal(Path.GetFullPath(other), Path.GetFullPath(vm.CurrentProject!.Root));
        Assert.Contains("outro projeto", vm.Toasts.Toasts.Last().Message, StringComparison.Ordinal);
        Assert.Empty(new NightBatchStore(ProjectFolder.Open(root)).Open());
    }

    [Fact]
    public async Task Sem_cliente_da_API_o_lote_avisa_que_falta_a_chave_e_nao_cria_tarefa()
    {
        var (vm, _) = await OpenAsync(withClient: false);

        await SendOvernightAsync(vm);

        Assert.Empty(vm.TaskService.Tasks);
        Assert.Contains("Falta a chave da IA", vm.Toasts.Toasts.Last().Message, StringComparison.Ordinal);
    }

}
