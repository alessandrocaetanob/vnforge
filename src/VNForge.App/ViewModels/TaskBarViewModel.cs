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

    /// <summary>
    /// Pergunta à pessoa antes de desfazer um lote noturno ("Desfazer o lote da Sala de aula?"); <see langword="false"/> mantém o lote. Sem ele, o "Desfazer" do lote
    /// roda direto. As demais tarefas em lote não perguntam.
    /// </summary>
    public Func<BackgroundTaskItem, Task<bool>>? ConfirmUndo { get; set; }

    private async Task UndoAsync(BackgroundTaskItem item)
    {
        if (item.IsBatch && ConfirmUndo is { } confirm && !await confirm(item))
        {
            return;
        }

        if (history is not null)
        {
            await history.UndoAsync(item);
        }
    }

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

    /// <summary>Quantos lotes noturnos estão em andamento.</summary>
    public int RunningBatchCount { get; private set; }

    /// <summary>
    /// Há lote noturno em andamento: a barra mostra o ícone da noite e "Tradução noturna: 1 em andamento" no lugar de "Tarefas (n)", e o mesmo botão abre a lista.
    /// </summary>
    public bool ShowsNightBatch => RunningBatchCount > 0;

    /// <summary>"Tradução noturna: 1 em andamento", com o total de lotes que ainda não voltaram.</summary>
    public string NightBatchText => $"Tradução noturna: {RunningBatchCount} em andamento";

    /// <summary>A tarefa que a barra mostra com título e progresso é um lote noturno: o chip da noite já a representa, então ela não se repete.</summary>
    public bool CurrentIsRunningBatch => Current is { IsBatch: true, IsRunning: true };

    /// <summary>A lista tem lote noturno: o cabeçalho lembra que fechar o app não o cancela.</summary>
    public bool HasBatch => Entries.Any(e => e.IsBatch);

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
        // IsBatch chega depois de a tarefa entrar na lista (o lote só vira "noturno" quando a API o aceita): o chip da barra depende dela.
        if (e.PropertyName is nameof(BackgroundTaskItem.State) or nameof(BackgroundTaskItem.IsBatch))
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
        RunningBatchCount = tasks.Count(t => t is { IsBatch: true, IsRunning: true });
        Current = tasks.LastOrDefault(t => t.IsRunning) ?? tasks.LastOrDefault();
        SyncEntries(tasks);
        OnPropertyChanged(nameof(TasksLabel));
        OnPropertyChanged(nameof(RunningBatchCount));
        OnPropertyChanged(nameof(ShowsNightBatch));
        OnPropertyChanged(nameof(NightBatchText));
        OnPropertyChanged(nameof(CurrentIsRunningBatch));
        OnPropertyChanged(nameof(HasBatch));
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
                entryByKey[key] = entry = new TaskEntry(key as BackgroundTaskGroup, clock, history is null ? null : UndoAsync);
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
