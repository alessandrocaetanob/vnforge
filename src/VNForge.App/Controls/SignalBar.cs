using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Barra de dois segmentos do cartão de Conferência: erros em <c>signal-error-fg</c> e avisos em <c>signal-warning-fg</c>, na proporção de cada um.
/// Sem nenhum dos dois, só o trilho (<c>seg-pending</c>).
/// </summary>
public sealed class SignalBar : Control
{
    public static readonly StyledProperty<int> ErrorsProperty = AvaloniaProperty.Register<SignalBar, int>(nameof(Errors));
    public static readonly StyledProperty<int> WarningsProperty = AvaloniaProperty.Register<SignalBar, int>(nameof(Warnings));

    static SignalBar()
    {
        AffectsRender<SignalBar>(ErrorsProperty, WarningsProperty);
    }

    public SignalBar() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public int Errors
    {
        get => GetValue(ErrorsProperty);
        set => SetValue(ErrorsProperty, value);
    }

    public int Warnings
    {
        get => GetValue(WarningsProperty);
        set => SetValue(WarningsProperty, value);
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

        using var _ = context.PushClip(new RoundedRect(bounds, bounds.Height / 2));
        context.FillRectangle(Brush("Vnf.SegPending"), bounds);

        var total = Math.Max(0, Errors) + Math.Max(0, Warnings);
        if (total == 0)
        {
            return;
        }

        var errorWidth = bounds.Width * Math.Max(0, Errors) / total;
        context.FillRectangle(Brush("Vnf.SignalErrorFg"), new Rect(0, 0, errorWidth, bounds.Height));
        context.FillRectangle(Brush("Vnf.SignalWarningFg"), new Rect(errorWidth, 0, bounds.Width - errorWidth, bounds.Height));
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Transparent;
}
