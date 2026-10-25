using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

public class BackgroundTaskTests
{
    [Fact]
    public async Task Tarefa_concluida_vai_a_100_por_cento_e_soma_o_custo()
    {
        var service = new BackgroundTaskService();

        var item = service.Start("Traduzir", (reporter, _) =>
        {
            reporter.Report(0.5, "metade");
            reporter.AddCost(0.25m);
            reporter.AddCost(1.1m);
            return Task.CompletedTask;
        });
        await item.Completion;

        Assert.Equal(BackgroundTaskState.Completed, item.State);
        Assert.Equal(100, item.Percent);
        Assert.Equal("R$ 7,29", item.CostText);
        Assert.Equal("Concluída", item.StateText);
        Assert.False(item.IsRunning);
    }

    [Fact]
    public async Task Cancelar_interrompe_a_tarefa_e_marca_como_cancelada()
    {
        var service = new BackgroundTaskService();
        var started = new TaskCompletionSource();

        var item = service.Start("Traduzir", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        await started.Task;
        Assert.True(item.CancelCommand.CanExecute(null));
        item.CancelCommand.Execute(null);
        await item.Completion;

        Assert.Equal(BackgroundTaskState.Cancelled, item.State);
        Assert.False(item.CancelCommand.CanExecute(null));
        item.CancelCommand.Execute(null);
    }

    [Fact]
    public async Task Falha_guarda_a_mensagem_e_nao_derruba_o_servico()
    {
        var service = new BackgroundTaskService();

        var item = service.Start("Build", (_, _) => throw new InvalidOperationException("disco cheio"));
        await item.Completion;

        Assert.Equal(BackgroundTaskState.Failed, item.State);
        Assert.Equal("disco cheio", item.Error);
        Assert.Equal("Falhou", item.StateText);
    }

    [Fact]
    public async Task Progresso_nulo_e_indeterminado_e_o_valor_e_limitado_a_0_e_1()
    {
        var service = new BackgroundTaskService();
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var item = service.Start("Extrair", async (reporter, _) =>
        {
            reporter.Report(null);
            Assert.True(service.Tasks[0].IsIndeterminate);
            reporter.Report(7);
            Assert.Equal(100, service.Tasks[0].Percent);
            reporter.Report(-3);
            Assert.Equal(0, service.Tasks[0].Percent);
            reported.SetResult();
            await release.Task;
        });
        await reported.Task;
        release.SetResult();
        await item.Completion;
    }

    [Fact]
    public async Task Report_depois_do_fim_nao_altera_a_tarefa_e_a_falha_vai_para_a_mensagem()
    {
        var service = new BackgroundTaskService();
        IBackgroundTaskReporter? late = null;
        var item = service.Start("Build", (reporter, _) =>
        {
            late = reporter;
            reporter.Report(0.5, "montando");
            throw new InvalidOperationException("sem espaço");
        });
        await item.Completion;

        late!.Report(0.1, "tarde");
        late.AddCost(5);

        Assert.Equal("sem espaço", item.Message);
        Assert.Equal(0, item.Cost);
    }

    [Fact]
    public async Task Start_volta_enquanto_o_trabalho_ainda_esta_bloqueado()
    {
        var service = new BackgroundTaskService();
        var release = new ManualResetEventSlim();

        var item = service.Start("Traduzir", (_, _) =>
        {
            release.Wait();
            return Task.CompletedTask;
        });

        // Se o trabalho rodasse na thread de quem inicia, este ponto nunca seria alcançado: quem edita outro script não espera a tradução.
        Assert.True(item.IsRunning);
        Assert.False(item.Completion.IsCompleted);
        release.Set();
        await item.Completion;
        Assert.Equal(BackgroundTaskState.Completed, item.State);
    }

    [Fact]
    public async Task As_mudancas_passam_pelo_despachante()
    {
        var posted = 0;
        var service = new BackgroundTaskService(a =>
        {
            posted++;
            a();
        });

        var item = service.Start("Traduzir", (reporter, _) =>
        {
            reporter.Report(0.1);
            reporter.AddCost(1);
            return Task.CompletedTask;
        });
        await item.Completion;

        Assert.Equal(3, posted);
    }

    [Fact]
    public async Task Limpar_remove_so_as_tarefas_terminadas()
    {
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var done = service.Start("A", (_, _) => Task.CompletedTask);
        await done.Completion;
        var running = service.Start("B", (_, _) => gate.Task);

        service.ClearFinished();

        Assert.Equal([running], service.Tasks);
        gate.SetResult();
        await running.Completion;
    }

    [Fact]
    public async Task Barra_mostra_a_tarefa_em_andamento_e_depois_a_ultima_terminada()
    {
        var service = new BackgroundTaskService();
        var bar = new TaskBarViewModel(service);
        Assert.False(bar.HasTasks);
        Assert.Null(bar.Current);

        var gate1 = new TaskCompletionSource();
        var gate2 = new TaskCompletionSource();
        var first = service.Start("Extrair", (_, _) => gate1.Task);
        Assert.Same(first, bar.Current);
        Assert.False(bar.HasOthersRunning);

        var second = service.Start("Traduzir", (_, _) => gate2.Task);
        Assert.Same(second, bar.Current);
        Assert.Equal("+1 em andamento", bar.OthersRunningText);

        gate2.SetResult();
        await second.Completion;
        Assert.Same(first, bar.Current);

        gate1.SetResult();
        await first.Completion;
        Assert.Same(second, bar.Current);
        Assert.Equal(0, bar.RunningCount);

        bar.ClearFinishedCommand.Execute(null);
        Assert.False(bar.HasTasks);
    }

    [Fact]
    public void A_janela_principal_expoe_a_barra_de_tarefas()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(
            new StartViewModel(new VNForge.Core.Projects.RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), TimeProvider.System));

        Assert.NotNull(vm.Tasks);
        Assert.Same(vm.Tasks.Service, vm.TaskService);
    }
}
