using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

public sealed partial class ShortcutMapView : UserControl
{
    public ShortcutMapView() => InitializeComponent();

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseShortcutMapCommand.Execute(null);
        }
    }
}
