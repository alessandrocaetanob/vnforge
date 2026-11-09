using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.Core.Review;

namespace VNForge.App.Controls;

/// <summary>
/// A barra da fila por risco: alto em <c>signal-error</c>, médio em <c>signal-warning</c> e baixo em <c>status-pending</c>, do começo para o fim. Tem 8 px de
/// altura e cantos redondos, como a <see cref="Seg3Bar"/>; a largura é a que a linha dá. A cor nunca está sozinha: a legenda ao lado diz o nome e o percentual.
/// </summary>
public sealed class RiskBar : Control
{
    public const double BarHeight = 8;

    public static readonly StyledProperty<RiskDistribution?> ValueProperty = AvaloniaProperty.Register<RiskBar, RiskDistribution?>(nameof(Value));

    static RiskBar() => AffectsRender<RiskBar>(ValueProperty);

    public RiskBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public RiskDistribution? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var track = new RoundedRect(new Rect(0, 0, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Brush("Vnf.SegPending"), null, track);
        if (Value is not { } value)
        {
            return;
        }

        using var _ = context.PushClip(track);
        var x = 0.0;
        foreach (var (share, key) in new[] { (value.High, "Vnf.SignalErrorFg"), (value.Medium, "Vnf.SignalWarningFg"), (value.Low, "Vnf.StatusPendingFg") })
        {
            var part = width * share / 100.0;
            if (part > 0)
            {
                context.DrawRectangle(Brush(key), null, new Rect(x, 0, part, BarHeight));
                x += part;
            }
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}
