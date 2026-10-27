using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Views;

/// <summary>Só monta o original com os marcadores em selos (um <c>TextBlock</c> não faz isso por binding) e leva o foco à tela, para as teclas valerem; a lógica fica no <see cref="ReviewModeViewModel"/>.</summary>
public sealed partial class ReviewModeView : UserControl
{
    private readonly HashSet<Key> held = [];
    private ReviewModeViewModel? current;
    private WindowBase? watchedWindow;
    private CancellationTokenSource? entering;

    public ReviewModeView()
    {
        InitializeComponent();

        // Segurar Enter, M ou uma seta repete o KeyDown: sem o filtro de held a revisão aprovaria ou pularia uma fala atrás da outra.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => held.Clear();
    }

    /// <summary>Enter aprova, E corrige, M deixa para depois, a seta para a direita pula, a da esquerda volta e Esc sai. Só a primeira batida de cada tecla vale.</summary>
    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (current is null || e.KeyModifiers != KeyModifiers.None || IsModalOpen())
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

    /// <summary>O mapa de atalhos e a janela Traduzir com IA ficam por cima da tela: as teclas são deles enquanto estão abertos.</summary>
    private bool IsModalOpen() =>
        TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel { IsShortcutMapOpen: true } or MainWindowViewModel { Translate.IsOpen: true };

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e) => held.Remove(e.Key);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (current is not null)
        {
            current.PropertyChanged -= OnViewModelPropertyChanged;
        }

        held.Clear();
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
        entering?.Cancel();
        if (watchedWindow is not null)
        {
            watchedWindow.Deactivated -= OnWindowDeactivated;
            watchedWindow = null;
        }

        base.OnDetachedFromVisualTree(e);
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

    /// <summary>A janela perdeu a ativação com uma tecla segurada: o KeyUp não vem, e a tecla ficaria presa como "já apertada".</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e) => held.Clear();

    private void FocusScreen() => Dispatcher.UIThread.Post(() => Root.Focus(), DispatcherPriority.Input);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewModeViewModel.Detail))
        {
            FillSource();
        }
        else if (e.PropertyName == nameof(ReviewModeViewModel.Position))
        {
            _ = PlayEnterAsync();
        }
    }

    /// <summary>Ao trocar de linha, o roteiro e o palco entram com um fade de 140 ms; sem animações no Windows, a troca é imediata.</summary>
    private async Task PlayEnterAsync()
    {
        if (!MotionPreferences.AnimationsEnabled || !this.IsAttachedToVisualTree())
        {
            return;
        }

        // O fade anterior só é cancelado depois que este assume o campo, para o finally dele não repor a opacidade.
        var previous = entering;
        using var source = new CancellationTokenSource();
        entering = source;
        if (previous is not null)
        {
            await previous.CancelAsync();
        }

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(140),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0.35) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };

        try
        {
            await Task.WhenAll(fade.RunAsync(Script, source.Token), fade.RunAsync(Stage, source.Token));
        }
        catch (OperationCanceledException)
        {
            // Outra troca de linha no meio do fade o recomeça.
        }
        finally
        {
            if (entering == source)
            {
                Script.Opacity = 1;
                Stage.Opacity = 1;
                entering = null;
            }
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
