using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class BuildView : UserControl
{
    private NotifyCollectionChangedEventHandler? follow;
    private BuildViewModel? followed;

    public BuildView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => FollowLog();
    }

    /// <summary>Mantém o fim do log à vista enquanto linhas novas chegam.</summary>
    private void FollowLog()
    {
        if (followed is not null && follow is not null)
        {
            followed.Log.CollectionChanged -= follow;
        }

        followed = DataContext as BuildViewModel;
        if (followed is null)
        {
            return;
        }

        follow = (_, _) => Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
        followed.Log.CollectionChanged += follow;
    }
}
