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
    public async Task Enviar_o_lote_grava_nada_ainda_e_poe_na_lista_uma_tarefa_do_lote_em_andamento()
    {
        var (vm, root) = await OpenAsync();

        await SendOvernightAsync(vm);

        Assert.Equal(1, client.Creates);
        var task = Assert.Single(vm.TaskService.Tasks);
        Assert.Equal("Tradução noturna · Esta cena", task.Title);
        Assert.True(task.IsBatch);
        Assert.True(task.IsRunning);
        Assert.StartsWith("Lote enviado às ", task.Message, StringComparison.Ordinal);
        Assert.Contains("2 linhas", task.Message, StringComparison.Ordinal);
        Assert.Contains("caprichada", task.Message, StringComparison.Ordinal);
        Assert.NotNull(task.CheckNowCommand);
        Assert.All(ProjectFolder.Open(root).LoadText("yst00001").Where(u => u.Status != TranslationStatus.Approved), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
        Assert.Single(new NightBatchStore(ProjectFolder.Open(root)).Open());
    }

    [Fact]
    public async Task Verificar_agora_depois_que_o_lote_terminou_grava_a_traducao_e_deixa_o_Desfazer()
    {
        var (vm, root) = await OpenAsync();
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();

        await FinishAsync(vm, client, task);

        var saved = ProjectFolder.Open(root).LoadText("yst00001");
        Assert.Equal(TranslationStatus.Machine, saved[0].Status);
        Assert.Equal("[PT] 原文0", saved[0].Translation);
        Assert.Equal(BackgroundTaskState.Completed, task.State);
        Assert.StartsWith("Chegou às ", task.Message, StringComparison.Ordinal);
        Assert.Contains("2 linhas gravadas", task.Message, StringComparison.Ordinal);
        Assert.NotNull(task.Restore);
        Assert.Equal(2, task.Restore.LineCount);
        Assert.Null(task.CheckNowCommand);
        Assert.Empty(new NightBatchStore(ProjectFolder.Open(root)).Open());
        Assert.Contains("2 linhas traduzidas pela IA", vm.Toasts.Toasts.Last().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Linhas_que_voltaram_com_erro_aparecem_na_tarefa_com_Ver_as_linhas()
    {
        var (vm, _) = await OpenAsync();
        client.OutcomeOf = _ => MessageBatchOutcome.Errored;
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();

        await FinishAsync(vm, client, task);

        Assert.Contains("2 voltaram com erro", task.Message, StringComparison.Ordinal);
        Assert.Equal("Ver as 2 linhas", task.ShowLinesLabel);
        Assert.NotNull(task.ShowLinesCommand);
        Assert.Null(task.Restore);
        var entry = vm.Tasks.Entries.Single();
        Assert.True(entry.CanShowLines);
        Assert.Equal("Ver as 2 linhas", entry.ShowLinesLabel);
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
    public async Task Sem_a_chave_ao_retomar_a_tarefa_do_lote_aparece_com_o_motivo_e_o_lote_segue_na_API()
    {
        var (vm, root) = await OpenAsync(
            beforeOpen: async root => await new NightBatchService(client, ProjectFolder.Open(root)).SubmitAsync(
                new NightBatchOrder("O capítulo", ClaudeModels.Opus, ClaudeEffort.Medium, 20, new ExchangeRate(5m), key => key == "yst00001"),
                TestContext.Current.CancellationToken),
            factory: () => throw new InvalidOperationException("sem chave"));

        var task = Assert.Single(vm.TaskService.Tasks);
        await vm.NightBatchWatching;
        await task.Completion;

        Assert.Equal(BackgroundTaskState.Failed, task.State);
        Assert.StartsWith("Sem chave de API", task.Message, StringComparison.Ordinal);
        Assert.Contains("msgbatch_fake0001 segue na API", task.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.Cancels);
        Assert.Single(new NightBatchStore(ProjectFolder.Open(root)).Open());
    }

    [Fact]
    public async Task Verificacoes_que_falham_varias_vezes_seguidas_param_a_tarefa_com_o_motivo_e_o_lote_segue_na_API()
    {
        var (vm, root) = await OpenAsync();
        client.GetFails = new HttpRequestException("sem rede");
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();

        for (var i = 1; i < MainWindowViewModel.MaxCheckFailures; i++)
        {
            task.CheckNowCommand!.Execute(null);
        }

        await task.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(BackgroundTaskState.Failed, task.State);
        Assert.Equal(MainWindowViewModel.MaxCheckFailures, client.Gets);
        Assert.Contains("Não foi possível verificar o lote msgbatch_fake0001", task.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.Cancels);
        Assert.Single(new NightBatchStore(ProjectFolder.Open(root)).Open());
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
    public async Task Desfazer_o_lote_pergunta_antes_e_Manter_nao_muda_nada()
    {
        var (vm, root) = await OpenAsync();
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();
        await FinishAsync(vm, client, task);
        var entry = vm.Tasks.Entries.Single();

        var asking = entry.UndoCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.UndoBatch.IsOpen);

        Assert.Equal("Desfazer o lote \"Esta cena\"?", vm.UndoBatch.Title);
        Assert.Contains("Volta 2 linhas ao que eram antes do lote de ", vm.UndoBatch.Message, StringComparison.Ordinal);
        Assert.Equal("Desfazer 2 linhas", vm.UndoBatch.ConfirmLabel);
        vm.UndoBatch.KeepCommand.Execute(null);
        await asking;

        Assert.False(vm.UndoBatch.IsOpen);
        Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
        Assert.False(task.IsUndone);
    }

    [Fact]
    public async Task Desfazer_o_lote_confirmado_devolve_as_linhas_e_a_editada_depois_fica_como_esta()
    {
        var (vm, root) = await OpenAsync();
        await SendOvernightAsync(vm);
        var task = vm.TaskService.Tasks.Single();
        await FinishAsync(vm, client, task);
        new LineEditor(ProjectFolder.Open(root), TimeProvider.System).SetTranslation("yst00001", "yst00001:000001", "à mão");
        var entry = vm.Tasks.Entries.Single();

        var asking = entry.UndoCommand.ExecuteAsync(null);
        await WaitForAsync(() => vm.UndoBatch.IsOpen);

        Assert.Equal("1 linha", vm.UndoBatch.RestoreText);
        Assert.Equal("1 linha", vm.UndoBatch.KeptText);
        vm.UndoBatch.ConfirmCommand.Execute(null);
        await asking;

        var saved = ProjectFolder.Open(root).LoadText("yst00001");
        Assert.Equal(TranslationStatus.Untranslated, saved[0].Status);
        Assert.Equal("à mão", saved[1].Translation);
        Assert.True(task.IsUndone);
    }

    [Fact]
    public async Task Sem_cliente_da_API_o_lote_avisa_que_falta_a_chave_e_nao_cria_tarefa()
    {
        var (vm, _) = await OpenAsync(withClient: false);

        await SendOvernightAsync(vm);

        Assert.Empty(vm.TaskService.Tasks);
        Assert.Contains("Falta a chave da IA", vm.Toasts.Toasts.Last().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falha_ao_enviar_o_lote_mostra_o_motivo_em_portugues_e_nao_cria_tarefa()
    {
        var (vm, root) = await OpenAsync();
        client.CreateFails = new HttpRequestException("sem rede");

        await SendOvernightAsync(vm);

        Assert.Empty(vm.TaskService.Tasks);
        Assert.StartsWith("O lote não foi enviado: ", vm.Toasts.Toasts.Last().Message, StringComparison.Ordinal);
        Assert.Empty(new NightBatchStore(ProjectFolder.Open(root)).List());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "A condição não chegou a tempo.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
