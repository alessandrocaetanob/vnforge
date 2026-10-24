using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Services;

namespace VNForge.App.ViewModels;

/// <summary>Trecho da barra inferior que mostra a tarefa em andamento (ou a última que terminou), com progresso, custo e Cancelar.</summary>
public sealed partial class TaskBarViewModel : ObservableObject
{
    public TaskBarViewModel(BackgroundTaskService service)
    {
        Service = service;
        service.Tasks.CollectionChanged += OnTasksChanged;
        Refresh();
    }

    public BackgroundTaskService Service { get; }

    /// <summary>A tarefa mais recente ainda rodando; sem nenhuma rodando, a última da lista.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTasks), nameof(OthersRunningText), nameof(HasOthersRunning))]
    public partial BackgroundTaskItem? Current { get; private set; }

    public bool HasTasks => Current is not null;

    public int RunningCount => Service.Tasks.Count(t => t.IsRunning);

    public bool HasOthersRunning => OthersRunningText.Length > 0;

    /// <summary>"+2 em andamento" quando há mais de uma tarefa rodando ao mesmo tempo.</summary>
    public string OthersRunningText => RunningCount > 1 ? $"+{RunningCount - 1} em andamento" : "";

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
    }

    private void Refresh()
    {
        var tasks = Service.Tasks;
        Current = tasks.LastOrDefault(t => t.IsRunning) ?? tasks.LastOrDefault();
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(OthersRunningText));
        OnPropertyChanged(nameof(HasOthersRunning));
    }
}
