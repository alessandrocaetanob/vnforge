using Avalonia.Controls;
using Avalonia.Input.Platform;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ReleaseView : UserControl
{
    private ReleaseViewModel? followed;

    public ReleaseView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow();
    }

    private void Follow()
    {
        if (followed is not null)
        {
            followed.CopyRequested -= OnCopyRequested;
        }

        followed = DataContext as ReleaseViewModel;
        if (followed is not null)
        {
            followed.CopyRequested += OnCopyRequested;
        }
    }

    private async void OnCopyRequested(object? sender, string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A área de transferência pode estar travada por outro programa; copiar de novo costuma funcionar.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
