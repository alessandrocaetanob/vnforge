using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class BuildView : UserControl
{
    private readonly NotifyCollectionChangedEventHandler follow;
    private BuildViewModel? followed;

    public BuildView()
    {
        InitializeComponent();
        follow = (_, _) => Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
        DataContextChanged += (_, _) => FollowLog();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FollowLog();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unfollow();
    }

    /// <summary>Mantém o fim do log à vista enquanto linhas novas chegam; só enquanto a página está na tela, para não reter a view.</summary>
    private void FollowLog()
    {
        Unfollow();
        if (!this.IsAttachedToVisualTree() || DataContext is not BuildViewModel vm)
        {
            return;
        }

        followed = vm;
        vm.Log.CollectionChanged += follow;
    }

    private void Unfollow()
    {
        if (followed is not null)
        {
            followed.Log.CollectionChanged -= follow;
            followed = null;
        }
    }
}
