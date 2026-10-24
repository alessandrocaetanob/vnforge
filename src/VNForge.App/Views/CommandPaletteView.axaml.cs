using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Results.AddHandler(TappedEvent, OnResultTapped, RoutingStrategies.Bubble);
        Scrim.Tapped += (_, _) => Palette?.CloseCommand.Execute(null);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                Dispatcher.UIThread.Post(() => Input.Focus(), DispatcherPriority.Input);
            }
        };
    }

    private CommandPaletteViewModel? Palette => DataContext as CommandPaletteViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Palette is not { } palette)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                palette.SelectNext();
                break;
            case Key.Up:
                palette.SelectPrevious();
                break;
            case Key.Enter:
                palette.ExecuteSelected();
                break;
            case Key.Escape:
                palette.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is PaletteEntry entry)
        {
            Palette?.Execute(entry);
        }
    }
}
