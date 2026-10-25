using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Um valor em dinheiro (v3.css, <c>.money</c>): o real em primeiro lugar e, só quando <see cref="Usd"/> tem valor, o dólar em letra pequena ao lado.
/// As telas de uso deixam <see cref="Usd"/> vazio e mostram só o real; o dólar e a cotação aparecem em Ajustes e no Novo projeto.
/// </summary>
public sealed class CostLabel : TemplatedControl
{
    public static readonly StyledProperty<decimal> BrlProperty = AvaloniaProperty.Register<CostLabel, decimal>(nameof(Brl));
    public static readonly StyledProperty<decimal?> UsdProperty = AvaloniaProperty.Register<CostLabel, decimal?>(nameof(Usd));

    public static readonly DirectProperty<CostLabel, string> BrlTextProperty =
        AvaloniaProperty.RegisterDirect<CostLabel, string>(nameof(BrlText), o => o.BrlText);

    public static readonly DirectProperty<CostLabel, string?> UsdTextProperty =
        AvaloniaProperty.RegisterDirect<CostLabel, string?>(nameof(UsdText), o => o.UsdText);

    private string brlText = Money.Brl(0);
    private string? usdText;

    public decimal Brl
    {
        get => GetValue(BrlProperty);
        set => SetValue(BrlProperty, value);
    }

    public decimal? Usd
    {
        get => GetValue(UsdProperty);
        set => SetValue(UsdProperty, value);
    }

    public string BrlText
    {
        get => brlText;
        private set => SetAndRaise(BrlTextProperty, ref brlText, value);
    }

    /// <summary>O dólar em letra pequena; nulo nas telas de uso.</summary>
    public string? UsdText
    {
        get => usdText;
        private set => SetAndRaise(UsdTextProperty, ref usdText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BrlProperty || change.Property == UsdProperty)
        {
            BrlText = Money.Brl(Brl);
            UsdText = Usd is { } usd ? Money.Usd(usd) : null;
            AutomationProperties.SetName(this, UsdText is null ? BrlText : $"{BrlText} ({UsdText})");
        }
    }
}