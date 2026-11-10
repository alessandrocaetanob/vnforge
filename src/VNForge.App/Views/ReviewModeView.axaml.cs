using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
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
    private double squeezedNextHeight;
    private double squeezedPreviousHeight;
    private bool scrolledToTranslation;

    public ReviewModeView()
    {
        InitializeComponent();

        // Segurar Enter, M ou uma seta repete o KeyDown: sem o filtro de held a revisão aprovaria ou pularia uma fala atrás da outra.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => held.Clear();
        Script.LayoutUpdated += OnScriptLayoutUpdated;
        ActualThemeVariantChanged += (_, _) => FillSpeaker();
    }

    /// <summary>
    /// Com a fala anterior, o original, a tradução e a seguinte, o roteiro passa da altura de uma janela de 1366×768 quando a tradução é longa ou o texto está
    /// grande. Em vez de cortar na borda, a seguinte some primeiro e, se ainda não couber, a anterior; as duas voltam (a anterior primeiro) quando o roteiro
    /// cabe de novo, e <see cref="squeezedNextHeight"/> e <see cref="squeezedPreviousHeight"/> guardam o que ocupavam. Sem as duas e ainda alto demais, o roteiro
    /// rola até a tradução, para a fala a aprovar aparecer inteira. A conta usa a altura pedida pela pilha, porque a extensão do <c>ScrollViewer</c> nunca fica
    /// abaixo da área visível.
    /// </summary>
    private void OnScriptLayoutUpdated(object? sender, EventArgs e)
    {
        const double Tolerance = 0.5;
        var viewport = Script.Viewport.Height;
        var wanted = ScriptStack.DesiredSize.Height;
        if (viewport <= 0 || wanted <= 0)
        {
            return;
        }

        if (wanted > viewport + Tolerance)
        {
            if (NextBlock.IsVisible)
            {
                squeezedNextHeight = NextBlock.Bounds.Height;
                NextBlock.IsVisible = false;
            }
            else if (PreviousBlock.IsVisible)
            {
                squeezedPreviousHeight = PreviousBlock.Bounds.Height;
                PreviousBlock.IsVisible = false;
            }
            else if (!scrolledToTranslation)
            {
                scrolledToTranslation = true;
                TranslationCard.BringIntoView();
            }
        }
        else if (!PreviousBlock.IsVisible && wanted + squeezedPreviousHeight <= viewport + Tolerance)
        {
            PreviousBlock.IsVisible = true;
        }
        else if (!NextBlock.IsVisible && PreviousBlock.IsVisible && wanted + squeezedNextHeight <= viewport + Tolerance)
        {
            NextBlock.IsVisible = true;
        }
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
        ResetSqueeze();
        current = DataContext as ReviewModeViewModel;
        if (current is null)
        {
            return;
        }

        current.PropertyChanged += OnViewModelPropertyChanged;
        FillSource();
        FillSpeaker();
        FocusScreen();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        held.Clear();
        CancelEntering();
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

    private void ResetSqueeze()
    {
        NextBlock.IsVisible = true;
        PreviousBlock.IsVisible = true;
        squeezedNextHeight = 0;
        squeezedPreviousHeight = 0;
        scrolledToTranslation = false;
    }

    private void FocusScreen() => Dispatcher.UIThread.Post(() => Root.Focus(), DispatcherPriority.Input);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewModeViewModel.Detail))
        {
            FillSource();
            FillSpeaker();
        }
        else if (e.PropertyName == nameof(ReviewModeViewModel.SpeakerText))
        {
            FillSpeaker();
        }
        else if (e.PropertyName == nameof(ReviewModeViewModel.Position))
        {
            // A medida da fala seguinte é da linha antiga: recomeça do zero, e o layout esconde de novo se a nova não couber.
            ResetSqueeze();
            _ = PlayEnterAsync();
        }
    }

    /// <summary>Cancela o fade em andamento na thread da interface. O CancelAsync rodaria os callbacks da animação no thread pool, e o finally do fade poderia ver o campo já trocado e deixar nele uma fonte descartada.</summary>
    private void CancelEntering() => entering?.Cancel();

    /// <summary>Ao trocar de linha, o roteiro e o palco entram com um fade de 140 ms; sem animações no Windows, a troca é imediata.</summary>
    private async Task PlayEnterAsync()
    {
        if (!MotionPreferences.AnimationsEnabled || !this.IsAttachedToVisualTree())
        {
            return;
        }

        CancelEntering();
        using var source = new CancellationTokenSource();
        entering = source;
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

    /// <summary>
    /// Quem fala, como no quadro: o nome do jogo e a seta em cinza e o nome em português em destaque, na cor do personagem quando ele está cadastrado, seguido da voz.
    /// O texto é montado aqui porque só uma parte dele leva a cor. O menu de copiar o nome continua valendo para o bloco todo.
    /// </summary>
    private void FillSpeaker()
    {
        SpeakerLine.Inlines?.Clear();
        if (current?.Detail is not { } detail || SpeakerLine.Inlines is not { } inlines)
        {
            return;
        }

        if (detail.Speaker is null || detail.SpeakerTarget is null)
        {
            inlines.Add(new Run(detail.SpeakerPair));
        }
        else
        {
            inlines.Add(new Run($"{detail.Speaker} → "));
            var name = new Run(detail.SpeakerTarget) { FontWeight = FontWeight.SemiBold };
            if (detail.SpeakerColor is { } color)
            {
                name.Foreground = color.BrushFor(ActualThemeVariant);
            }

            inlines.Add(name);
        }

        if (detail.HasVoice)
        {
            inlines.Add(new Run($" · {detail.Voice}"));
        }
    }

    /// <summary>O selo de virar a página do modo revisão leva um chevron contornado em vez do triângulo cheio do tema (o desenho vem do modelo do <c>MarkerChip</c>, que não se troca daqui por estilo).</summary>
    private static void OnChipTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not MarkerChip chip || e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_Triangle") is not { } triangle)
        {
            return;
        }

        triangle.Data = Geometry.Parse("M0.5,1 L4,4.5 L7.5,1");
        triangle.Fill = null;
        triangle.StrokeThickness = 1.3;
        triangle.StrokeLineCap = PenLineCap.Round;
        triangle.StrokeJoin = PenLineJoin.Round;
        triangle[!Shape.StrokeProperty] = chip[!TemplatedControl.ForegroundProperty];
    }

    /// <summary>
    /// O original com os marcadores em selos. Os do fim do texto (a virada de página, quase sempre) vão para a fileira de baixo, ao lado de "Entender o japonês", como no
    /// quadro; os do meio ficam no texto, no lugar em que quebram a fala.
    /// </summary>
    private void FillSource()
    {
        SourceText.Inlines?.Clear();
        SourceMarkers.Children.Clear();
        if (current?.Detail is not { } detail)
        {
            return;
        }

        var segments = detail.SourceSegments.ToList();
        var tail = segments.Count;
        while (tail > 0 && segments[tail - 1].IsMarker)
        {
            tail--;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!segment.IsMarker)
            {
                SourceText.Inlines?.Add(new Run(segment.Text));
                continue;
            }

            var kind = segment.Text.StartsWith('{') ? MarkerKind.Variable : MarkerKind.Control;
            var chip = (MarkerChip)MarkerChipGenerator.CreateChip(segment.Text, kind);
            chip.TemplateApplied += OnChipTemplateApplied;
            if (i >= tail)
            {
                chip.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                SourceMarkers.Children.Add(chip);
            }
            else
            {
                SourceText.Inlines?.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
            }
        }
    }
}
