using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace VNForge.App.Controls;

/// <summary>
/// Barra do gasto com IA (v3.css, <c>.budget</c>): o que já foi gasto em <c>seg-ai</c>, o pedido que a pessoa está para fazer em <c>accent</c> e uma
/// marca no limite. Quando gasto mais pedido passam do limite o controle ganha <c>:over</c> e o pedido vira aviso. Valores em reais.
/// Com <see cref="IsInset"/> ela vira a <c>.bar2</c> do quadro do diálogo Traduzir com IA: o pedido em <c>seg-translated</c> sobre uma trilha <c>bg-inset</c> (no cartão de
/// mesmo fundo só os trechos coloridos aparecem) e a marca do limite só quando o total passa dele, pois a régua vai até o limite.
/// </summary>
public sealed class BudgetBar : TemplatedControl
{
    public const double BarHeight = 8;
    private const double MarkOverhang = 3;

    public static readonly StyledProperty<double> SpentProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Spent));
    public static readonly StyledProperty<double> AskProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Ask));
    public static readonly StyledProperty<double> LimitProperty = AvaloniaProperty.Register<BudgetBar, double>(nameof(Limit));
    public static readonly StyledProperty<bool> IsInsetProperty = AvaloniaProperty.Register<BudgetBar, bool>(nameof(IsInset));

    static BudgetBar()
    {
        AffectsRender<BudgetBar>(SpentProperty, AskProperty, LimitProperty, IsInsetProperty);
        AffectsMeasure<BudgetBar>(IsInsetProperty);
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

    /// <summary>A barra dentro de um cartão <c>bg-inset</c> (<c>.bar2</c> do quadro): trilha do fundo do cartão, pedido em <c>seg-translated</c>, marca só acima do limite.</summary>
    public bool IsInset
    {
        get => GetValue(IsInsetProperty);
        set => SetValue(IsInsetProperty, value);
    }

    /// <summary>Se o gasto somado ao pedido passa do limite.</summary>
    public bool IsOver => Limit > 0 && Spent + Ask > Limit;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width, IsInset ? BarHeight : BarHeight + (MarkOverhang * 2));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var scale = Math.Max(Limit, Spent + Ask);
        var top = IsInset ? 0 : MarkOverhang;
        var track = new RoundedRect(new Rect(0, top, width, BarHeight), BarHeight / 2);
        context.DrawRectangle(Brush(IsInset ? "Vnf.BgInset" : "Vnf.SegPending"), null, track);
        if (scale <= 0)
        {
            return;
        }

        using (context.PushClip(track))
        {
            var spent = width * Math.Clamp(Spent / scale, 0, 1);
            var ask = width * Math.Clamp(Ask / scale, 0, 1);
            context.DrawRectangle(Brush("Vnf.SegAi"), null, new Rect(0, top, spent, BarHeight));
            context.DrawRectangle(Brush(IsOver ? "Vnf.SignalWarningFg" : IsInset ? "Vnf.SegTranslated" : "Vnf.Accent"), null, new Rect(spent, top, ask, BarHeight));
        }

        if (IsInset ? IsOver : Limit > 0)
        {
            var x = Math.Round(width * Limit / scale);
            context.DrawLine(new Pen(Brush("Vnf.TextPrimary"), 2), new Point(x, top - MarkOverhang), new Point(x, top + BarHeight + MarkOverhang));
        }
    }

    private void UpdateState() => PseudoClasses.Set(":over", IsOver);

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.Gray;
}