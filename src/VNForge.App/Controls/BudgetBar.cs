using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Barra do gasto com IA (<c>.bar2</c> do quadro): o que já foi gasto em <c>seg-ai</c> e o pedido que a pessoa está para fazer em <c>seg-translated</c>, sobre uma trilha
/// <c>bg-inset</c> (no cartão de fundo <c>bg-inset</c> do quadro só os trechos coloridos aparecem). A régua vai até o limite, então não há marca enquanto cabe;
/// quando gasto mais pedido passam do limite o controle ganha <c>:over</c>, o pedido vira aviso e uma marca mostra onde o limite ficou. Valores em reais.
/// </summary>
public sealed class BudgetBar : TemplatedControl
{
    public const double BarHeight = 8;
    private const double MarkOverhang = 3;

    public static readonly StyledProperty<double> SpentProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Spent));
    public static readonly StyledProperty<double> AskProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Ask));
    public static readonly StyledProperty<double> LimitProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Limit));

    static BudgetBar()
    {
        AffectsRender<BudgetBar>(SpentProperty, AskProperty, LimitProperty);
        SpentProperty.Changed.AddClassHandler<BudgetBar>((bar, _) => bar.UpdateState());
        AskProperty.Changed.AddClassHandler<BudgetBar>((bar, _) => bar.UpdateState());
        LimitProperty.Changed.AddClassHandler<BudgetBar>((bar, _) => bar.UpdateState());
    }

    public double Spent
    {
        get => GetValue(SpentProperty);
        set => SetValue(SpentProperty, value);
    }

    /// <summary>O valor do pedido que a pessoa está para fazer; zero sem pedido.</summary>
    public double Ask
    {
        get => GetValue(AskProperty);
        set => SetValue(AskProperty, value);
    }

    /// <summary>O limite de gasto; zero ou menos desenha só a barra, sem marca.</summary>
    public double Limit
    {
        get => GetValue(LimitProperty);
        set => SetValue(LimitProperty, value);
    }

    /// <summary>Se o gasto somado ao pedido passa do limite.</summary>
    public bool IsOver => Limit > 0 && Spent + Ask > Limit;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var scale = Math.Max(Limit, Spent + Ask);
        var track = new RoundedRect(new Rect(0, 0, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Brush("Vnf.BgInset"), null, track);
        if (scale <= 0)
        {
            return;
        }

        using (context.PushClip(track))
        {
            var spent = width * Math.Clamp(Spent / scale, 0, 1);
            var ask = width * Math.Clamp(Ask / scale, 0, 1);
            context.DrawRectangle(Brush("Vnf.SegAi"), null, new Rect(0, 0, spent, BarHeight));
            context.DrawRectangle(Brush(IsOver ? "Vnf.SignalWarningFg" : "Vnf.SegTranslated"), null, new Rect(spent, 0, ask, BarHeight));
        }

        if (IsOver)
        {
            var x = Math.Round(width * Limit / scale);
            context.DrawLine(new Pen(Brush("Vnf.TextPrimary"), 2), new Point(x, -MarkOverhang), new Point(x, BarHeight + MarkOverhang));
        }
    }

    private void UpdateState() => PseudoClasses.Set(":over", IsOver);

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}