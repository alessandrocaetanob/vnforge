using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// A barra do cartão de custo ("Gerar a bíblia", v3.css <c>.bar2</c>): oito pixels de altura, o que o projeto já gastou em <c>seg-ai</c> e, logo depois, a fatia do
/// orçamento que o pedido usa em <c>seg-translated</c>. As duas partes são frações do orçamento (de 0 a 1); sem orçamento as duas ficam em zero e só a trilha aparece.
/// </summary>
public sealed class ShareBar : Control
{
    public const double BarHeight = 8;

    public static readonly StyledProperty<double> SpentProperty = AvaloniaProperty.Register<ShareBar, double>(nameof(Spent));
    public static readonly StyledProperty<double> AskProperty = AvaloniaProperty.Register<ShareBar, double>(nameof(Ask));

    static ShareBar() => AffectsRender<ShareBar>(SpentProperty, AskProperty);

    /// <summary>A fração do orçamento que o projeto já gastou.</summary>
    public double Spent
    {
        get => GetValue(SpentProperty);
        set => SetValue(SpentProperty, value);
    }

    /// <summary>A fração do orçamento que o pedido usa.</summary>
    public double Ask
    {
        get => GetValue(AskProperty);
        set => SetValue(AskProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var track = new RoundedRect(new Rect(0, 0, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Brush("Vnf.BgInset"), null, track);
        using (context.PushClip(track))
        {
            var spent = width * Math.Clamp(Spent, 0, 1);
            var ask = width * Math.Clamp(Ask, 0, 1 - Math.Clamp(Spent, 0, 1));
            context.DrawRectangle(Brush("Vnf.SegAi"), null, new Rect(0, 0, spent, BarHeight));
            context.DrawRectangle(Brush("Vnf.SegTranslated"), null, new Rect(spent, 0, ask, BarHeight));
        }
    }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}
