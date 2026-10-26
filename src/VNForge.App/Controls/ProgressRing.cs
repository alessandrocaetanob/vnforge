using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Anel de progresso em três estados (v3.css, <c>.ringwrap</c>): três arcos a partir do alto, no sentido horário (pronto, esperando revisão e, no
/// que sobra do círculo, o japonês que falta), e a parte pronta em porcentagem no centro. Com a classe <c>sm</c> ele tem 56 px e só o número.
/// O texto do centro vem do template (<c>ProgressRing.axaml</c>); o controle desenha os arcos.
/// </summary>
public sealed class ProgressRing : ThreeStateProgress
{
    public static readonly StyledProperty<string?> CaptionProperty = AvaloniaProperty.Register<ProgressRing, string?>(nameof(Caption), "pronto");

    public static readonly DirectProperty<ProgressRing, string> PercentTextProperty =
        AvaloniaProperty.RegisterDirect<ProgressRing, string>(nameof(PercentText), o => o.PercentText);

    private string percentText = "0%";

    static ProgressRing()
    {
        DoneProperty.Changed.AddClassHandler<ProgressRing>((ring, _) => ring.Refresh());
        TotalProperty.Changed.AddClassHandler<ProgressRing>((ring, _) => ring.Refresh());
    }

    /// <summary>A palavra sob o número ("pronto"); nula, o número fica sozinho.</summary>
    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string PercentText
    {
        get => percentText;
        private set => SetAndRaise(PercentTextProperty, ref percentText, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var stroke = size >= 100 ? size / 10 : Math.Max(4, size / 8);
        var radius = (size - stroke) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var done = DoneFraction;
        var review = ReviewFraction;

        Arc(context, center, radius, 0, 1, new Pen(Brush("Vnf.SegPending"), stroke));
        Arc(context, center, radius, 0, done, new Pen(Brush("Vnf.SegApproved"), stroke));
        Arc(context, center, radius, done, done + review, new Pen(Brush("Vnf.SegAi"), stroke));
    }

    /// <summary>Um arco de <paramref name="from"/> a <paramref name="to"/> (frações da volta), a partir do alto, no sentido horário.</summary>
    private static void Arc(DrawingContext context, Point center, double radius, double from, double to, Pen pen)
    {
        var sweep = to - from;
        if (sweep <= 0.0001)
        {
            return;
        }

        if (sweep >= 0.9999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointAt(center, radius, from), false);
            g.ArcTo(PointAt(center, radius, to), new Size(radius, radius), 0, sweep > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point center, double radius, double fraction)
    {
        var angle = (fraction * 2 * Math.PI) - (Math.PI / 2);
        return new Point(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
    }

    private void Refresh() => PercentText = DonePercentText();

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}