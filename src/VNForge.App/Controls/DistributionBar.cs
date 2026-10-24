using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.Core.Projects;

namespace VNForge.App.Controls;

/// <summary>
/// Barra empilhada da distribuição de status (docs/design/components/StatusDistribution): Aprovadas, Revisadas, Traduzidas, IA;
/// Pendentes é o trilho. As cores vêm dos tokens <c>seg-*</c> do tema ativo.
/// </summary>
public sealed class DistributionBar : Control
{
    public static readonly StyledProperty<StatusDistribution?> ValueProperty =
        AvaloniaProperty.Register<DistributionBar, StatusDistribution?>(nameof(Value));

    static DistributionBar()
    {
        AffectsRender<DistributionBar>(ValueProperty);
    }

    public DistributionBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public StatusDistribution? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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

        if (Value is not { } v)
        {
            return;
        }

        var x = 0.0;
        foreach (var (percent, key) in new[]
        {
            (v.Approved, "Vnf.SegApproved"),
            (v.Reviewed, "Vnf.SegReviewed"),
            (v.Translated, "Vnf.SegTranslated"),
            (v.Machine, "Vnf.SegAi"),
        })
        {
            var width = bounds.Width * percent / 100.0;
            if (width > 0)
            {
                context.FillRectangle(Brush(key), new Rect(x, 0, width, bounds.Height));
                x += width;
            }
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
