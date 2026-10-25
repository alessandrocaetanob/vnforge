using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.AI.Cost;

namespace VNForge.App.Services;

public enum BackgroundTaskState
{
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>O que o trabalho de uma tarefa usa para avisar a barra inferior. Pode ser chamado de qualquer thread.</summary>
public interface IBackgroundTaskReporter
{
    /// <summary>Progresso de 0 a 1; <c>null</c> deixa a barra indeterminada. A mensagem, se vier, substitui a anterior.</summary>
    void Report(double? fraction, string? message = null);

    /// <summary>Soma ao custo da tarefa, em dólares.</summary>
    void AddCost(decimal usd);
}

/// <summary>Uma tarefa longa (extrair, traduzir, build, injetar). As propriedades só mudam na thread da interface.</summary>
public sealed partial class BackgroundTaskItem : ObservableObject
{
    private readonly CancellationTokenSource cts;
    private readonly Func<ExchangeRate> rate;

    internal BackgroundTaskItem(string title, CancellationTokenSource cts, Func<ExchangeRate>? rate = null)
    {
        Title = title;
        this.cts = cts;
        this.rate = rate ?? (() => ExchangeRate.Placeholder);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public string Title { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>Completa quando a tarefa termina, de qualquer jeito; nunca lança.</summary>
    public Task Completion { get; internal set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(StateText))]
    public partial BackgroundTaskState State { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(Percent))]
    public partial double? Progress { get; internal set; }

    [ObservableProperty]
    public partial string Message { get; internal set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostText))]
    public partial decimal Cost { get; internal set; }

    [ObservableProperty]
    public partial string? Error { get; internal set; }

    public bool IsRunning => State == BackgroundTaskState.Running;

    public bool IsIndeterminate => Progress is null;

    /// <summary>De 0 a 100, para a barra de progresso.</summary>
    public double Percent => Math.Round((Progress ?? 0) * 100, 1);

    /// <summary>O gasto da tarefa em reais, com a cotação que a pessoa informou nos Ajustes; a tarefa relata em dólar.</summary>
    /// <remarks>Com a cotação de exemplo (a pessoa ainda não informou a dela) o valor sai com "≈" e a marca "cotação de exemplo".</remarks>
    public string CostText
    {
        get
        {
            var current = rate();
            var text = Controls.Money.BrlEstimate(current.ToBrl(Cost));
            return current.IsInformed || Cost <= 0m ? text : $"≈ {text} (cotação de exemplo)";
        }
    }

    internal void RefreshCostText() => OnPropertyChanged(nameof(CostText));

    public string StateText => State switch
    {
        BackgroundTaskState.Running => "Em andamento",
        BackgroundTaskState.Completed => "Concluída",
        BackgroundTaskState.Cancelled => "Cancelada",
        _ => "Falhou",
    };

    private void Cancel()
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A tarefa acabou entre o clique e o cancelamento; não há mais o que cancelar.
        }
    }

    partial void OnStateChanged(BackgroundTaskState value) => CancelCommand.NotifyCanExecuteChanged();
}

/// <summary>
/// Roda tarefas longas fora da thread da interface e publica o andamento de cada uma. As mudanças de estado
/// passam por <c>post</c> (na janela, o <c>Dispatcher</c>), então o trabalho pode relatar de qualquer thread.
/// </summary>
public sealed class BackgroundTaskService(Action<Action>? dispatch = null)
{
    private readonly Action<Action> post = dispatch ?? (a => a());
    private ExchangeRate rate = ExchangeRate.Placeholder;

    public ObservableCollection<BackgroundTaskItem> Tasks { get; } = [];

    /// <summary>A cotação que converte o custo das tarefas em reais; trocá-la atualiza o texto das tarefas na lista.</summary>
    public ExchangeRate Rate
    {
        get => rate;
        set
        {
            rate = value ?? throw new ArgumentNullException(nameof(value));
            foreach (var task in Tasks)
            {
                task.RefreshCostText();
            }
        }
    }

    /// <summary>Inicia a tarefa e volta na hora; chame da thread da interface, que é dona de <see cref="Tasks"/>. use <see cref="BackgroundTaskItem.Completion"/> para esperar o fim.</summary>
    public BackgroundTaskItem Start(string title, Func<IBackgroundTaskReporter, CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        var item = new BackgroundTaskItem(title, cts, () => rate) { State = BackgroundTaskState.Running };
        Tasks.Add(item);
        item.Completion = Task.Run(() => RunAsync(item, work, cts));
        return item;
    }

    /// <summary>Tira da lista as tarefas que já terminaram. Chame da thread da interface.</summary>
    public void ClearFinished()
    {
        foreach (var item in Tasks.Where(t => !t.IsRunning).ToList())
        {
            Tasks.Remove(item);
        }
    }

    private async Task RunAsync(BackgroundTaskItem item, Func<IBackgroundTaskReporter, CancellationToken, Task> work, CancellationTokenSource cts)
    {
        var state = BackgroundTaskState.Completed;
        string? error = null;
        try
        {
            await work(new Reporter(this, item), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            state = BackgroundTaskState.Cancelled;
        }
        catch (Exception ex)
        {
            state = BackgroundTaskState.Failed;
            error = ex.Message;
        }
        finally
        {
            cts.Dispose();
        }

        post(() =>
        {
            item.Error = error;
            item.Message = error ?? "";
            if (state == BackgroundTaskState.Completed)
            {
                item.Progress = 1;
            }

            item.State = state;
        });
    }

    private sealed class Reporter(BackgroundTaskService service, BackgroundTaskItem item) : IBackgroundTaskReporter
    {
        private readonly object gate = new();
        private decimal cost;

        public void Report(double? fraction, string? message = null)
        {
            var clamped = fraction is { } f ? Math.Clamp(f, 0, 1) : (double?)null;
            service.post(() =>
            {
                if (!item.IsRunning)
                {
                    return;
                }

                item.Progress = clamped;
                if (message is not null)
                {
                    item.Message = message;
                }
            });
        }

        public void AddCost(decimal usd)
        {
            decimal total;
            lock (gate)
            {
                cost += usd;
                total = cost;
            }

            service.post(() =>
            {
                if (item.IsRunning)
                {
                    item.Cost = total;
                }
            });
        }
    }
}
