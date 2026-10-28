using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>Trecho da barra inferior que mostra a tarefa em andamento (ou a última que terminou), com progresso, custo e Cancelar.</summary>
public sealed partial class TaskBarViewModel : ObservableObject
{
    private readonly TimeProvider clock;
    private readonly Dictionary<object, TaskEntry> entryByKey = [];

    private readonly BatchHistory? history;

    /// <param name="history">Dá o "Desfazer" às tarefas em lote; sem ele a lista não mostra o botão.</param>
    public TaskBarViewModel(BackgroundTaskService service, TimeProvider? clock = null, BatchHistory? history = null)
    {
        Service = service;
        this.history = history;
        this.clock = clock ?? TimeProvider.System;
        service.Tasks.CollectionChanged += OnTasksChanged;
        Refresh();
    }

    public BackgroundTaskService Service { get; }

    /// <summary>As linhas da lista: a tarefa em andamento no topo, as terminadas abaixo, da mais recente para a mais antiga. As etapas de um teste no jogo formam uma linha só.</summary>
    public ObservableCollection<TaskEntry> Entries { get; } = [];

    /// <summary>A tarefa mais recente ainda rodando; sem nenhuma rodando, a última da lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTasks))]
    public partial BackgroundTaskItem? Current { get; private set; }

    public bool HasTasks => Current is not null;

    public int RunningCount { get; private set; }

    public bool HasOthersRunning => RunningCount > 1;

    /// <summary>"+2 em andamento" quando há mais de uma tarefa rodando ao mesmo tempo.</summary>
    public string OthersRunningText => HasOthersRunning ? $"+{RunningCount - 1} em andamento" : "";

    /// <summary>"Tarefas (2)": quantas linhas a lista guarda, em andamento ou terminadas.</summary>
    public string TasksLabel => $"Tarefas ({Entries.Count})";

    /// <summary>Há o que o "Limpar concluídas" tire da lista.</summary>
    public bool HasFinished => Service.Tasks.Any(t => !t.IsRunning);

    [RelayCommand]
    private void ClearFinished() => Service.ClearFinished();

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BackgroundTaskItem item in e.OldItems)
            {
                item.PropertyChanged -= OnItemChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (BackgroundTaskItem item in e.NewItems)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }

        Refresh();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackgroundTaskItem.State))
        {
            Refresh();
        }
        else if (sender is BackgroundTaskItem item && entryByKey.TryGetValue((object?)item.Group ?? item, out var entry))
        {
            entry.Notify();
        }
    }

    private void Refresh()
    {
        var tasks = Service.Tasks;
        RunningCount = tasks.Count(t => t.IsRunning);
        Current = tasks.LastOrDefault(t => t.IsRunning) ?? tasks.LastOrDefault();
        SyncEntries(tasks);
        OnPropertyChanged(nameof(TasksLabel));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(OthersRunningText));
        OnPropertyChanged(nameof(HasOthersRunning));
    }

    /// <summary>Junta as etapas de um grupo numa linha, reaproveita as linhas que já existiam (a expansão fica) e reordena.</summary>
    private void SyncEntries(IReadOnlyList<BackgroundTaskItem> tasks)
    {
        var buckets = new Dictionary<object, List<BackgroundTaskItem>>();
        foreach (var task in tasks)
        {
            var key = (object?)task.Group ?? task;
            if (!buckets.TryGetValue(key, out var items))
            {
                buckets[key] = items = [];
            }

            items.Add(task);
        }

        foreach (var gone in entryByKey.Keys.Where(k => !buckets.ContainsKey(k)).ToList())
        {
            entryByKey.Remove(gone);
        }

        foreach (var (key, items) in buckets)
        {
            if (!entryByKey.TryGetValue(key, out var entry))
            {
                entryByKey[key] = entry = new TaskEntry(key as BackgroundTaskGroup, clock, history is null ? null : history.UndoAsync);
            }

            entry.Update(items);
        }

        // Do fim para o começo da lista: a ordenação é estável, então de dois empatados fica em cima o que entrou depois.
        var ordered = buckets.Keys.Reverse()
            .Select(k => entryByKey[k])
            .OrderByDescending(e => e.IsRunning)
            .ThenByDescending(e => e.SortTime)
            .ToList();
        if (!ordered.SequenceEqual(Entries))
        {
            Entries.Clear();
            foreach (var entry in ordered)
            {
                Entries.Add(entry);
            }
        }
    }
}
