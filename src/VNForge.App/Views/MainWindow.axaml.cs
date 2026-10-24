using Avalonia.Controls;
using Avalonia.Input;
using VNForge.App.Controls;

namespace VNForge.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
            {
                UpdateMicaFallback();
            }
        };
        UpdateMicaFallback();
        Motion.Follow(this);
        AddHandler(KeyDownEvent, OnShortcutKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += (_, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel vm && !vm.TryClose())
            {
                e.Cancel = true;
            }
        };
    }

    /// <summary>
    /// <c>?</c> abre e fecha o mapa de atalhos; <c>Esc</c> fecha. Num campo de texto o <c>?</c> é só o caractere digitado.
    /// </summary>
    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape && vm.IsShortcutMapOpen)
        {
            vm.CloseShortcutMapCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeySymbol == "?" && FocusManager?.GetFocusedElement() is not (TextBox or AvaloniaEdit.Editing.TextArea))
        {
            vm.ToggleShortcutMapCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>O Windows desliga a transparência em sessão remota e em economia de bateria; sem Mica, a reserva bg-base cobre o fundo.</summary>
    private void UpdateMicaFallback() =>
        MicaFallback.IsVisible = ActualTransparencyLevel != WindowTransparencyLevel.Mica;
}
