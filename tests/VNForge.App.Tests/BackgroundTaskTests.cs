using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

public class BackgroundTaskTests
{

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
}
