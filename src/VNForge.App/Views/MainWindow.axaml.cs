using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
        Closing += OnClosing;
    }

    /// <summary>Quanto o resumo da sessão fica na tela antes de a janela fechar sozinha; fechar de novo antes disso fecha na hora.</summary>
    public static readonly TimeSpan CloseSummaryTime = TimeSpan.FromMilliseconds(2500);

    private bool closeSummaryShown;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm)
        {
            return;
        }

        if (!vm.TryClose())
        {
            e.Cancel = true;
            return;
        }

        // O resumo só adia o fechamento pedido pela pessoa, uma vez: o desligamento do Windows, o fim do app e um segundo pedido de fechar não esperam.
        if (closeSummaryShown || e.CloseReason != WindowCloseReason.WindowClosing || !vm.ShowCloseSummary())
        {
            return;
        }

        closeSummaryShown = true;
        e.Cancel = true;
        DispatcherTimer.RunOnce(() =>
        {
            if (IsVisible)
            {
                Close();
            }
        }, CloseSummaryTime);
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
