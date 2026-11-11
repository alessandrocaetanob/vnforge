using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace VNForge.App.ViewModels;

/// <summary>Coleção que troca o conteúdo inteiro com uma única notificação <c>Reset</c>, em vez de uma por item (cada Insert ou RemoveAt no meio custa O(n)).</summary>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
