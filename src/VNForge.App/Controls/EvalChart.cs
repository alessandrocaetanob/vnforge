using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>
/// O gráfico "Custo e qualidade" do banco de provas: custo do jogo todo (R$) no eixo horizontal e chrF no vertical, um ponto por configuração rotulado com a letra
/// da tabela. A cor sozinha não distingue as configurações: a letra vai dentro do ponto, e a escolhida para o M4 ganha um anel.
/// </summary>
public sealed class EvalChart : Control
{
    public static readonly StyledProperty<IEnumerable<EvalChartPoint>?> ItemsSourceProperty = AvaloniaProperty.Register<EvalChart, IEnumerable<EvalChartPoint>?>(nameof(ItemsSource));

    private const double Left = 40;
    private const double Right = 12;
    private const double Top = 10;
    private const double Bottom = 40;
    private const double Radius = 10;
    private const double AxisFontSize = 11;
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private INotifyCollectionChanged? watching;

    static EvalChart()
    {
        AffectsRender<EvalChart>(ItemsSourceProperty);
        ItemsSourceProperty.Changed.AddClassHandler<EvalChart>((chart, e) => chart.Watch(e.NewValue as IEnumerable<EvalChartPoint>));
    }

    public IEnumerable<EvalChartPoint>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 220 : Math.Min(availableSize.Height, 220));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var points = ItemsSource?.ToList() ?? [];
        var plot = new Rect(Left, Top, Math.Max(1, Bounds.Width - Left - Right), Math.Max(1, Bounds.Height - Top - Bottom));
        var grid = new Pen(Brush("Vnf.StrokeSubtle", Brushes.Gray), 1);
        var muted = Brush("Vnf.TextSecondary", Brushes.Gray);

        var (xMax, xStep) = XScale(points);
        var (yMin, yMax, yStep) = YScale(points);

        for (var y = yMin; y <= yMax + 0.001; y += yStep)
        {
            var at = plot.Bottom - ((y - yMin) / (yMax - yMin) * plot.Height);
            context.DrawLine(grid, new Point(plot.Left, at), new Point(plot.Right, at));
            Text(context, y.ToString("0", Pt), muted, new Point(plot.Left - 6, at), right: true, middle: true);
        }

        for (var x = 0.0; x <= xMax + 0.001; x += xStep)
        {
            var at = plot.Left + (x / xMax * plot.Width);
            Text(context, x.ToString("0", Pt), muted, new Point(at, plot.Bottom + 6), center: true);
        }

        Text(context, "Custo do jogo todo (R$)", muted, new Point(plot.Left + (plot.Width / 2), plot.Bottom + 22), center: true);
        Text(context, "chrF", muted, new Point(plot.Left - 6, plot.Top - 10), right: true);

        var accent = Brush("Vnf.Accent", Brushes.SeaGreen);
        var other = Brush("Vnf.SegAi", Brushes.SteelBlue);
        foreach (var point in points)
        {
            var centre = new Point(
                plot.Left + (Math.Clamp(point.Cost / xMax, 0, 1) * plot.Width),
                plot.Bottom - (Math.Clamp((point.Chrf - yMin) / (yMax - yMin), 0, 1) * plot.Height));
            if (point.IsChosen)
            {
                context.DrawEllipse(null, new Pen(accent, 2), centre, Radius + 4, Radius + 4);
            }

            context.DrawEllipse(point.IsChosen ? accent : other, null, centre, Radius, Radius);
            Text(context, point.Letter, Brushes.White, centre, center: true, middle: true, bold: true);
        }
    }

    private static (double Max, double Step) XScale(IReadOnlyList<EvalChartPoint> points)
    {
        var max = points.Count == 0 ? 0 : points.Max(p => p.Cost);
        var step = max switch
        {
            <= 5 => 1,
            <= 20 => 5,
            <= 100 => 25,
            <= 400 => 50,
            _ => 100,
        };
        return (Math.Max(step, Math.Ceiling(max * 1.05 / step) * step), step);
    }

    private static (double Min, double Max, double Step) YScale(IReadOnlyList<EvalChartPoint> points)
    {
        if (points.Count == 0)
        {
            return (0, 100, 25);
        }

        var min = Math.Floor((points.Min(p => p.Chrf) - 2) / 5) * 5;
        var max = Math.Ceiling((points.Max(p => p.Chrf) + 2) / 5) * 5;
        min = Math.Max(0, min);
        if (max - min < 10)
        {
            max = min + 10;
        }

        return (min, max, 5);
    }

    private void Text(DrawingContext context, string text, IBrush brush, Point at, bool right = false, bool middle = false, bool center = false, bool bold = false)
    {
        var formatted = new FormattedText(text, Pt, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), AxisFontSize, brush);
        var x = right ? at.X - formatted.Width : center ? at.X - (formatted.Width / 2) : at.X;
        var y = middle ? at.Y - (formatted.Height / 2) : at.Y;
        context.DrawText(formatted, new Point(x, y));
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    private void Watch(IEnumerable<EvalChartPoint>? items)
    {
        if (watching is not null)
        {
            watching.CollectionChanged -= OnChanged;
        }

        watching = items as INotifyCollectionChanged;
        if (watching is not null)
        {
            watching.CollectionChanged += OnChanged;
        }
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
