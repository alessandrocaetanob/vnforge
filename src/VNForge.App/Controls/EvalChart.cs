using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using VNForge.App.ViewModels;

namespace VNForge.App.Controls;

/// <summary>
/// O gráfico "Custo e qualidade" do banco de provas: custo do jogo todo (R$) no eixo horizontal e chrF no vertical, um ponto por configuração com a letra da tabela
/// em cima. Os pontos são azuis; a escolhida para o M4 é verde, maior e com anel. A cor sozinha não distingue as configurações: a letra vai junto de cada ponto.
/// </summary>
public sealed class EvalChart : Control
{
    public static readonly StyledProperty<IEnumerable<EvalChartPoint>?> ItemsSourceProperty = AvaloniaProperty.Register<EvalChart, IEnumerable<EvalChartPoint>?>(nameof(ItemsSource));

    private const double Left = 46;
    private const double Right = 10;
    private const double Top = 28;
    private const double Bottom = 40;
    private const double DotRadius = 6;
    private const double ChosenRadius = 7.5;
    private const double LetterRise = 16.5;
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
        new(double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 208 : Math.Min(availableSize.Height, 208));

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
            Text(context, y.ToString("0", Pt), muted, new Point(plot.Left - 9, at), TextAlignment.Right, middle: true);
        }

        foreach (var x in XTicks(xMax, xStep))
        {
            var at = plot.Left + (x / xMax * plot.Width);
            Text(context, x.ToString("0", Pt), muted, new Point(at, plot.Bottom + 8), TextAlignment.Center);
        }

        Text(context, "Custo do jogo todo (R$)", muted, new Point(plot.Left + (plot.Width / 2), plot.Bottom + 24), TextAlignment.Center);
        var title = Format("chrF", muted, bold: false);
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(7, plot.Top + (plot.Height / 2))))
        {
            context.DrawText(title, new Point(-title.Width / 2, -title.Height / 2));
        }

        var accent = Brush("Vnf.Accent", Brushes.SeaGreen);
        var ring = accent is ISolidColorBrush solid ? new ImmutableSolidColorBrush(solid.Color, 0.4) : accent;
        var other = Brush("Vnf.SegTranslated", Brushes.SteelBlue);
        var ink = Brush("Vnf.TextPrimary", Brushes.White);
        foreach (var point in points)
        {
            var centre = new Point(
                plot.Left + (Math.Clamp(point.Cost / xMax, 0, 1) * plot.Width),
                plot.Bottom - (Math.Clamp((point.Chrf - yMin) / (yMax - yMin), 0, 1) * plot.Height));
            if (point.IsChosen)
            {
                context.DrawEllipse(null, new Pen(ring, 3), centre, ChosenRadius + 1.5, ChosenRadius + 1.5);
            }

            var radius = point.IsChosen ? ChosenRadius : DotRadius;
            context.DrawEllipse(point.IsChosen ? accent : other, null, centre, radius, radius);
            Text(context, point.Letter, ink, new Point(centre.X, centre.Y - LetterRise), TextAlignment.Center, middle: true, bold: true);
        }
    }

    /// <summary>
    /// O fim do eixo do custo e o passo das marcas. O fim é o maior custo mais 5%, arredondado para cima a um quinto do passo; as marcas vão só até onde cabem
    /// (o maior custo de 156,70 dá um eixo até 170 com marcas em 0, 50, 100 e 150).
    /// </summary>
    public static (double Max, double Step) XScale(IReadOnlyList<EvalChartPoint> points)
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
        var grain = step <= 1 ? 1 : step / 5;
        return (Math.Max(step, Math.Ceiling(max * 1.05 / grain) * grain), step);
    }

    /// <summary>As marcas do eixo do custo: de zero até o último múltiplo do passo que cabe no eixo.</summary>
    public static IReadOnlyList<double> XTicks(double max, double step)
    {
        List<double> ticks = [];
        for (var x = 0.0; x <= max + 0.001; x += step)
        {
            ticks.Add(x);
        }

        return ticks;
    }

    private static (double Min, double Max, double Step) YScale(List<EvalChartPoint> points)
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

    private static FormattedText Format(string text, IBrush brush, bool bold) =>
        new(text, Pt, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), AxisFontSize, brush);

    private static void Text(DrawingContext context, string text, IBrush brush, Point at, TextAlignment align = TextAlignment.Left, bool middle = false, bool bold = false)
    {
        var formatted = Format(text, brush, bold);
        var x = align switch
        {
            TextAlignment.Right => at.X - formatted.Width,
            TextAlignment.Center => at.X - (formatted.Width / 2),
            _ => at.X,
        };
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
