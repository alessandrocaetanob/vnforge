using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>
/// Uma linha da lista de tarefas: uma tarefa avulsa ou um grupo de etapas (um teste no jogo) com hora e resultado.
/// As etapas aparecem quando a linha do grupo é expandida.
/// </summary>
public sealed partial class TaskEntry : ObservableObject
{
    private readonly TimeProvider clock;
    private readonly Func<BackgroundTaskItem, Task>? undo;

    internal TaskEntry(BackgroundTaskGroup? group, TimeProvider clock, Func<BackgroundTaskItem, Task>? undo = null)
    {
        Group = group;
        this.clock = clock;
        this.undo = undo;
    }

    /// <summary>O grupo que a linha representa; <c>null</c> na tarefa avulsa.</summary>
    public BackgroundTaskGroup? Group { get; }

    /// <summary>As etapas do grupo na ordem em que começaram; na tarefa avulsa, só ela.</summary>
    public IReadOnlyList<BackgroundTaskItem> Steps { get; private set; } = [];

    public bool IsGroup => Group is not null;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string Title => Group?.Title ?? Steps[0].Title;

    /// <summary>
    /// O resultado da linha. No grupo, vale a última tentativa de cada etapa: o Build que falhou e foi refeito com sucesso não mancha o teste.
    /// Qualquer etapa em andamento deixa o grupo em andamento; depois dela, a falha pesa mais que o cancelamento.
    /// </summary>
    public BackgroundTaskState State
    {
        get
        {
            var latest = Steps.GroupBy(s => s.Title).Select(g => g.Last().State).ToList();
            if (latest.Contains(BackgroundTaskState.Running))
            {
                return BackgroundTaskState.Running;
            }

            if (latest.Contains(BackgroundTaskState.Failed))
            {
                return BackgroundTaskState.Failed;
            }

            return latest.Contains(BackgroundTaskState.Cancelled) ? BackgroundTaskState.Cancelled : BackgroundTaskState.Completed;
        }
    }

    public bool IsRunning => State == BackgroundTaskState.Running;

    public bool IsCompleted => State == BackgroundTaskState.Completed;

    public bool IsFailed => State == BackgroundTaskState.Failed;

    public bool IsCancelled => State == BackgroundTaskState.Cancelled;

    public string StateText => BackgroundTaskItem.DescribeState(State);

    public DateTimeOffset StartedAt => Steps.Min(s => s.StartedAt);

    /// <summary>O instante que ordena a lista: quando começou, enquanto roda; o do último passo, depois que terminou.</summary>
    public DateTimeOffset SortTime => IsRunning ? StartedAt : Steps.Max(s => s.FinishedAt ?? s.StartedAt);

    /// <summary>"14:32" no dia de hoje; em outro dia, "25/10 14:32".</summary>
    public string TimeText => FormatTime(StartedAt, clock.GetLocalNow());

    public string StepsText => Steps.Count == 1 ? "1 etapa" : $"{Steps.Count} etapas";

    /// <summary>A segunda linha: a hora e, na tarefa avulsa, a mensagem; no grupo, a etapa em andamento ou quantas etapas rodaram.</summary>
    public string Subtitle
    {
        get
        {
            if (IsGroup)
            {
                var running = Steps.LastOrDefault(s => s.IsRunning);
                return $"{TimeText} · {(running is null ? StepsText : running.Title)}";
            }

            var message = Steps[0].Message;
            if (IsBatch && message.Length > 0)
            {
                return message;
            }

            return string.IsNullOrEmpty(message) ? TimeText : $"{TimeText} · {message}";
        }
    }

    /// <summary>O custo só aparece na tarefa avulsa que usa a IA; o grupo de teste no jogo nunca gasta.</summary>
    public bool ShowsCost => !IsGroup && Steps[0].UsesAi;

    public string CostText => Steps[0].CostText;

    /// <summary>O custo vai à direita do título só na tarefa avulsa de IA que não é lote: a linha do lote já traz o custo na segunda linha ("... · caprichada · R$ 13,46").</summary>
    public bool ShowsCostBeside => ShowsCost && !IsBatch;

    /// <summary>A tarefa em lote deixou um ponto de restauração que ainda não foi usado: a linha mostra "Desfazer".</summary>
    public bool CanUndo => !IsGroup && Steps[0] is { Restore: not null, IsUndone: false, IsRunning: false };

    public bool IsBatch => !IsGroup && Steps[0].IsBatch;

    /// <summary>A linha de andamento do lote ("1.951 de 3.147 linhas prontas · verificado há 2 min"); só enquanto roda.</summary>
    public string Detail => !IsGroup && Steps[0].IsRunning ? Steps[0].Detail : string.Empty;

    public bool HasDetail => Detail.Length > 0;

    public double Percent => Steps[0].Percent;

    public bool ShowsProgress => IsBatch && Steps[0].IsRunning;

    public IRelayCommand? CheckNowCommand => !IsGroup && Steps[0].IsRunning ? Steps[0].CheckNowCommand : null;

    public bool CanCheckNow => CheckNowCommand is not null;

    /// <summary>"Cancelar lote" na linha do lote em andamento.</summary>
    public bool CanCancelBatch => IsBatch && Steps[0].IsRunning;

    public IRelayCommand CancelBatchCommand => Steps[0].CancelCommand;

    public IRelayCommand? ShowLinesCommand => !IsGroup && !Steps[0].IsRunning ? Steps[0].ShowLinesCommand : null;

    public bool CanShowLines => ShowLinesCommand is not null;

    public bool HasBatchActions => CanCheckNow || CanCancelBatch || CanShowLines;

    public string ShowLinesLabel => Steps[0].ShowLinesLabel;

    /// <summary>O texto do botão de desfazer: "Desfazer lote" no lote noturno, "Desfazer" nas demais.</summary>
    public string UndoLabel => IsBatch ? "Desfazer lote" : "Desfazer";

    /// <summary>O que o "Desfazer" fez (quantas linhas voltaram e quantas ficaram por terem sido editadas depois); vazio antes de usar.</summary>
    public string UndoMessage => Steps[0].UndoMessage;

    public bool HasUndoMessage => !IsGroup && UndoMessage.Length > 0;

    private bool CanRunUndo() => CanUndo && !Steps[0].IsUndoing;

    [RelayCommand(CanExecute = nameof(CanRunUndo))]
    private async Task Undo()
    {
        if (undo is not null && !IsGroup)
        {
            await undo(Steps[0]);
        }
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    internal void Update(IReadOnlyList<BackgroundTaskItem> steps)
    {
        Steps = steps;
        Notify();
    }

    /// <summary>Avisa a lista de que algo mudou numa etapa: título, estado, mensagem e custo são derivados dela.</summary>
    internal void Notify()
    {
        OnPropertyChanged(string.Empty);
        UndoCommand.NotifyCanExecuteChanged();
    }

    internal static string FormatTime(DateTimeOffset when, DateTimeOffset now) =>
        when.ToString(when.Date == now.Date ? "HH:mm" : "dd/MM HH:mm", CultureInfo.InvariantCulture);
}
