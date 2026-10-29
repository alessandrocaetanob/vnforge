using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Leva o foco à tela e traduz as teclas em comandos; a lógica fica no <see cref="ReadSceneViewModel"/>.</summary>
public sealed partial class ReadSceneView : UserControl
{
    private readonly HashSet<Key> held = [];
    private ReadSceneViewModel? current;
    private WindowBase? watchedWindow;

    public ReadSceneView()
    {
        InitializeComponent();

        // Segurar Espaço ou uma seta repete o KeyDown: sem o filtro de held a leitura passaria por várias falas de uma vez.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => held.Clear();
    }

    /// <summary>Espaço e a seta para a direita avançam, a da esquerda volta, F marca "soa estranho" e Esc sai. Só a primeira batida de cada tecla vale.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (current is null || e.KeyModifiers != KeyModifiers.None || IsModalOpen())
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Space or Key.Right => current.NextCommand,
            Key.Left => current.BackCommand,
            Key.F => current.ToggleOddCommand,
            Key.Escape => current.CloseCommand,
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        e.Handled = true;
        if (held.Add(e.Key) && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    /// <summary>O mapa de atalhos e a janela Traduzir com IA ficam por cima da tela: as teclas são deles enquanto estão abertos.</summary>
    private bool IsModalOpen() =>
        TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel { IsShortcutMapOpen: true } or MainWindowViewModel { Translate.IsOpen: true };

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e) => held.Remove(e.Key);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        held.Clear();
        current = DataContext as ReadSceneViewModel;
        if (current is not null)
        {
            FocusScreen();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is WindowBase window)
        {
            watchedWindow = window;
            window.Deactivated += OnWindowDeactivated;
        }

        FocusScreen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        held.Clear();
        if (watchedWindow is not null)
        {
            watchedWindow.Deactivated -= OnWindowDeactivated;
            watchedWindow = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>A janela perdeu a ativação com uma tecla segurada: o KeyUp não vem, e a tecla ficaria presa como "já apertada".</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e) => held.Clear();

    private void FocusScreen() => Dispatcher.UIThread.Post(() => Root.Focus(), DispatcherPriority.Input);
}
