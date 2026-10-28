using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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

    /// <summary>Abre ou fecha "Detalhes técnicos" por código; os testes de tela usam para alcançar o build, a injeção e o registro.</summary>
    public bool TechnicalExpanded
    {
        get => Technical.IsExpanded;
        set => Technical.IsExpanded = value;
    }

    /// <summary>Copia o log inteiro, com data e hora, para a área de transferência; é o que o usuário cola ao relatar um problema.</summary>
    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BuildViewModel vm || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(vm.LogText);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A área de transferência pode estar travada por outro programa; copiar de novo costuma funcionar.
            vm.ReportCopyFailure(ex.Message);
        }
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
