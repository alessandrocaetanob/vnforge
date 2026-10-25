using Avalonia;
using Avalonia.Controls;

namespace VNForge.App.Controls;

/// <summary>
/// Cartão de qualidade da tradução (v3.css, <c>.qcards .opt</c>): o <c>OptionCard</c> com o preço em reais e o tempo em destaque e o nome do modelo em letra pequena.
/// É um <see cref="RadioButton"/>: a escolha entre Rápida, Equilibrada e Caprichada é um grupo. O modelo só aparece aqui em letra pequena; o exato fica em Ajustes.
/// </summary>
public sealed class QualityCard : RadioButton
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<QualityCard, string?>(nameof(Title));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<QualityCard, string?>(nameof(Description));
    public static readonly StyledProperty<string?> ModelProperty = AvaloniaProperty.Register<QualityCard, string?>(nameof(Model));
    public static readonly StyledProperty<string?> DurationProperty = AvaloniaProperty.Register<QualityCard, string?>(nameof(Duration));
    public static readonly StyledProperty<decimal> PriceProperty = AvaloniaProperty.Register<QualityCard, decimal>(nameof(Price));

    public static readonly StyledProperty<bool> ShowPriceProperty = AvaloniaProperty.Register<QualityCard, bool>(nameof(ShowPrice), defaultValue: true);

    public static readonly DirectProperty<QualityCard, string> PriceTextProperty =
        AvaloniaProperty.RegisterDirect<QualityCard, string>(nameof(PriceText), o => o.PriceText);

    private string priceText = Money.Brl(0);

    protected override Type StyleKeyOverride => typeof(QualityCard);

    /// <summary>"Rápida", "Equilibrada" ou "Caprichada".</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>O nome do modelo, em letra pequena.</summary>
    public string? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>O tempo estimado para o escopo escolhido ("cerca de 6 min").</summary>
    public string? Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>O preço em reais do escopo escolhido.</summary>
    public decimal Price
    {
        get => GetValue(PriceProperty);
        set => SetValue(PriceProperty, value);
    }

    /// <summary>Esconde o preço e o tempo quando ainda não há linhas para estimar (Ajustes sem projeto aberto).</summary>
    public bool ShowPrice
    {
        get => GetValue(ShowPriceProperty);
        set => SetValue(ShowPriceProperty, value);
    }

    public string PriceText
    {
        get => priceText;
        private set => SetAndRaise(PriceTextProperty, ref priceText, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PriceProperty)
        {
            PriceText = Money.BrlEstimate(Price);
        }
    }
}