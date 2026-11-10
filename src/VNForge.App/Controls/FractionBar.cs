using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Faixa de andamento do quadro da Biblioteca: o trilho e o trecho cheio, de 0 a 1, com a altura e o raio do uso. É a faixa de 5 px no pé da capa (<c>.v3-cprog</c>, sem raio, o corte é o da capa)
/// e a barra de 6 px da gaveta e da varredura (<c>.pbar</c>, raio de 3). O trecho cheio é <c>accent</c> e o trilho <c>bg-inset</c>, salvo o que o uso pedir.
/// </summary>
public sealed class FractionBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<FractionBar, double>(nameof(Value));
    public static readonly StyledProperty<double> RadiusProperty = AvaloniaProperty.Register<FractionBar, double>(nameof(Radius));
    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<FractionBar, IBrush?>(nameof(Track));

    static FractionBar() => AffectsRender<FractionBar>(ValueProperty, RadiusProperty, TrackProperty);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>O trilho; nulo vale <c>bg-inset</c>.</summary>
    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width, double.IsNaN(Height) ? 6 : Height);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var full = new Rect(Bounds.Size);
        var track = Track ?? Brush("Vnf.BgInset");
        var fill = Brush("Vnf.Accent");
        using (context.PushClip(new RoundedRect(full, Radius)))
        {
            context.DrawRectangle(track, null, full);
            var share = double.IsNaN(Value) ? 0 : Math.Clamp(Value, 0, 1);
            if (share > 0)
            {
                context.DrawRectangle(fill, null, new Rect(0, 0, Bounds.Width * share, Bounds.Height));
            }
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}
