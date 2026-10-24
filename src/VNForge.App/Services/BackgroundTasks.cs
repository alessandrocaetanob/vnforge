using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    // Formato pt-BR fixo, como em ProjectLabels: não depende da cultura da máquina.
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly CancellationTokenSource cts;

    internal BackgroundTaskItem(string title, CancellationTokenSource cts)
    {
        Title = title;
        this.cts = cts;
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

    public string CostText => string.Create(PtBr, $"US$ {Cost:N2}");

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

    public ObservableCollection<BackgroundTaskItem> Tasks { get; } = [];

    /// <summary>Inicia a tarefa e volta na hora; use <see cref="BackgroundTaskItem.Completion"/> para esperar o fim.</summary>
    public BackgroundTaskItem Start(string title, Func<IBackgroundTaskReporter, CancellationToken, Task> work)
    {
        var cts = new CancellationTokenSource();
        var item = new BackgroundTaskItem(title, cts) { State = BackgroundTaskState.Running };
        Tasks.Add(item);
        item.Completion = Task.Run(() => RunAsync(item, work, cts));
        return item;
    }

    /// <summary>Tira da lista as tarefas que já terminaram.</summary>
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

            service.post(() => item.Cost = total);
        }
    }
}
