using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using VNForge.App.Services;

namespace VNForge.App.Controls;

/// <summary>
/// Troca de página do app: a nova entra com fade e 8 px de deslocamento vertical em 150 ms. Sem animações no Windows
/// (<see cref="MotionPreferences.AnimationsEnabled"/>), a troca é imediata.
/// </summary>
public sealed class FadeSlideTransition : IPageTransition
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    public const double Offset = 8;

    public static Animation CreateEnter() => new()
    {
        Duration = Duration,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, Offset) },
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) },
            },
        },
    };

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (from is not null)
        {
            from.IsVisible = false;
        }

        if (to is null)
        {
            return;
        }

        to.IsVisible = true;
        if (!MotionPreferences.AnimationsEnabled || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        to.RenderTransform = new TranslateTransform();
        try
        {
            await CreateEnter().RunAsync(to, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Trocar de página no meio da transição cancela a anterior; o finally deixa a página no estado final.
        }
        finally
        {
            to.RenderTransform = null;
            to.Opacity = 1;
        }
    }
}
