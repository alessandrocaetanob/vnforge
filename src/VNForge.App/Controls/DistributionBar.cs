using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.Controls;

/// <summary>
/// Barra empilhada da distribuição de status (docs/design/components/StatusDistribution): Aprovadas, Revisadas, Traduzidas, IA;
/// Pendentes é o trilho. As cores vêm dos tokens <c>seg-*</c> do tema ativo. Ao mudar o <see cref="Value"/> com a barra na tela, os segmentos
/// deslizam até os novos tamanhos (<see cref="AnimationDuration"/>); sem animações no Windows a troca é imediata.
/// </summary>
public sealed class DistributionBar : Control
{
    public static readonly StyledProperty<StatusDistribution?> ValueProperty =
        AvaloniaProperty.Register<DistributionBar, StatusDistribution?>(nameof(Value));

    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(400);

    private static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<DistributionBar, double>(nameof(Progress), 1);

    private double[] from = new double[Segments];
    private double[] target = new double[Segments];
    private CancellationTokenSource? running;

    private const int Segments = 4;

    private static readonly string[] Keys = ["Vnf.SegApproved", "Vnf.SegReviewed", "Vnf.SegTranslated", "Vnf.SegAi"];

    static DistributionBar()
    {
        AffectsRender<DistributionBar>(ProgressProperty);
        ValueProperty.Changed.AddClassHandler<DistributionBar>((bar, _) => bar.OnValueChanged());
    }

    public DistributionBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public StatusDistribution? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private double Progress => GetValue(ProgressProperty);

    /// <summary>Verdadeiro enquanto os segmentos deslizam até o novo <see cref="Value"/>.</summary>
    public bool IsSliding { get; private set; }

    private void OnValueChanged()
    {
        running?.Cancel();
        running = null;
        var shown = Shown();
        var next = Value is { } v ? new double[] { v.Approved, v.Reviewed, v.Translated, v.Machine } : new double[Segments];
        from = shown;
        target = next;
        IsSliding = false;

        if (!MotionPreferences.AnimationsEnabled || VisualRoot is null || shown.SequenceEqual(next))
        {
            SetCurrentValue(ProgressProperty, 1);
            InvalidateVisual();
            return;
        }

        SetCurrentValue(ProgressProperty, 0);
        _ = Animate();
    }

    private async Task Animate()
    {
        var source = running = new CancellationTokenSource();
        IsSliding = true;
        var animation = new Animation
        {
            Duration = AnimationDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(ProgressProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(ProgressProperty, 1d) } },
            },
        };
        try
        {
            await animation.RunAsync(this, source.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (running == source)
        {
            IsSliding = false;
            SetCurrentValue(ProgressProperty, 1);
        }
    }

    private double[] Shown()
    {
        var t = Progress;
        return [.. from.Zip(target, (a, b) => a + ((b - a) * t))];
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, double.IsNaN(Height) ? 8 : Height);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var radius = bounds.Height / 2;
        using var _ = context.PushClip(new RoundedRect(bounds, radius));
        context.FillRectangle(Brush("Vnf.SegPending"), bounds);

        var x = 0.0;
        var shown = Shown();
        for (var i = 0; i < Segments; i++)
        {
            var width = bounds.Width * shown[i] / 100.0;
            if (width > 0)
            {
                context.FillRectangle(Brush(Keys[i]), new Rect(x, 0, width, bounds.Height));
                x += width;
            }
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
