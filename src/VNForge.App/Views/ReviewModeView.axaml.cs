using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using VNForge.App.Controls;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Só monta o original com os marcadores em selos (um <c>TextBlock</c> não faz isso por binding) e leva o foco à tela, para as teclas valerem; a lógica fica no <see cref="ReviewModeViewModel"/>.</summary>
public sealed partial class ReviewModeView : UserControl
{
    private readonly HashSet<Key> held = [];
    private ReviewModeViewModel? current;

    public ReviewModeView()
    {
        InitializeComponent();

        // Segurar Enter, M ou uma seta repete o KeyDown: sem o filtro de held a revisão aprovaria ou pularia uma fala atrás da outra.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => held.Clear();
    }

    /// <summary>Enter aprova, E corrige, M deixa para depois, a seta para a direita pula, a da esquerda volta e Esc sai. Só a primeira batida de cada tecla vale.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (current is null || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var command = e.Key switch
        {
            Key.Enter => current.ApproveCommand,
            Key.E => current.CorrectCommand,
            Key.M => current.LaterCommand,
            Key.Right => current.SkipCommand,
            Key.Left => current.BackCommand,
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

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e) => held.Remove(e.Key);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (current is not null)
        {
            current.PropertyChanged -= OnViewModelPropertyChanged;
        }

        current = DataContext as ReviewModeViewModel;
        if (current is null)
        {
            return;
        }

        current.PropertyChanged += OnViewModelPropertyChanged;
        FillSource();
        FocusScreen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        held.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusScreen();
    }

    private void FocusScreen() => Dispatcher.UIThread.Post(() => Root.Focus(), DispatcherPriority.Input);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewModeViewModel.Detail))
        {
            FillSource();
        }
    }

    private void FillSource()
    {
        SourceText.Inlines?.Clear();
        if (current?.Detail is not { } detail)
        {
            return;
        }

        foreach (var segment in detail.SourceSegments)
        {
            if (!segment.IsMarker)
            {
                SourceText.Inlines?.Add(new Run(segment.Text));
                continue;
            }

            var kind = segment.Text.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control;
            SourceText.Inlines?.Add(new InlineUIContainer(MarkerChipGenerator.CreateChip(segment.Text, kind)) { BaselineAlignment = BaselineAlignment.Center });
        }
    }
}
