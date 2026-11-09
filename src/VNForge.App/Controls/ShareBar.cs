using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Barra de 8 px que mostra uma fração de 0 a 1: o trilho em <c>bg-inset</c> e o trecho cheio em <c>accent</c>, ou na cor que o uso pedir (o chrF da tabela de provas
/// usa <c>seg-translated</c>). Sem animação e sem texto; quem usa dá o nome acessível.
/// </summary>
public sealed class ShareBar : Control
{
    public const double BarHeight = 8;

    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ShareBar, double>(nameof(Value));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<ShareBar, IBrush?>(nameof(Track));

    static ShareBar() => AffectsRender<ShareBar>(ValueProperty, FillProperty, TrackProperty);

    /// <summary>A fração cheia, de 0 a 1; fora disso a barra fica vazia ou cheia.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>A cor do trecho cheio; nulo vale <c>accent</c>.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>A cor do trilho; nulo vale <c>bg-inset</c>.</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var track = new RoundedRect(new Rect(0, 0, Bounds.Width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Track ?? Brush("Vnf.BgInset"), null, track);
        var share = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
        if (share <= 0)
        {
            return;
        }

        using (context.PushClip(track))
        {
            context.DrawRectangle(Fill ?? Brush("Vnf.Accent"), null, new Rect(0, 0, Bounds.Width * share, BarHeight));
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}
