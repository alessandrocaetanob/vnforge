using Avalonia;
using Avalonia.Controls.Primitives;

namespace VNForge.App.Controls;

/// <summary>
/// Base do <see cref="ProgressRing"/> e da <see cref="Seg3Bar"/>: o progresso em três estados que as telas de uso mostram, a mesma fonte para os dois.
/// <see cref="Done"/> é o que está pronto, <see cref="Review"/> o que a IA traduziu e espera revisão; o que sobra de <see cref="Total"/> ainda está em japonês.
/// </summary>
public abstract class ThreeStateProgress : TemplatedControl
{
    public static readonly StyledProperty<double> DoneProperty = AvaloniaProperty.Register<ThreeStateProgress, double>(nameof(Done));
    public static readonly StyledProperty<double> ReviewProperty = AvaloniaProperty.Register<ThreeStateProgress, double>(nameof(Review));
    public static readonly StyledProperty<double> TotalProperty = AvaloniaProperty.Register<ThreeStateProgress, double>(nameof(Total));

    static ThreeStateProgress()
    {
        AffectsRender<ThreeStateProgress>(DoneProperty, ReviewProperty, TotalProperty);
    }

    public double Done
    {
        get => GetValue(DoneProperty);
        set => SetValue(DoneProperty, value);
    }

    public double Review
    {
        get => GetValue(ReviewProperty);
        set => SetValue(ReviewProperty, value);
    }

    public double Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    /// <summary>A parte pronta, de 0 a 1.</summary>
    public double DoneFraction => Fraction(Done);

    /// <summary>A parte que a IA traduziu e espera revisão, de 0 a 1, sem passar do que sobra depois da parte pronta.</summary>
    public double ReviewFraction => Math.Min(Fraction(Review), 1 - DoneFraction);

    /// <summary>A parte pronta como número inteiro de 0 a 100, arredondada para baixo: 99,9% nunca vira "100%".</summary>
    public int Percent => (int)Math.Floor(DoneFraction * 100);

    /// <summary>
    /// A parte pronta como texto: inteiro a partir de 1%, uma casa decimal abaixo disso ("0,7%") e "&lt;0,1%" quando só há uma fração mínima pronta.
    /// Assim "51 de 6.854 prontas" não vira "0%" ao lado de linhas que já saíram do japonês. Sempre arredondado para baixo.
    /// </summary>
    protected string DonePercentText()
    {
        var tenths = Total <= 0 ? 0 : (int)Math.Floor(Math.Clamp(Done, 0, Total) * 1000 / Total);
        if (tenths >= 10)
        {
            return $"{tenths / 10}%";
        }

        if (tenths == 0)
        {
            return Done > 0 && Total > 0 ? "<0,1%" : "0%";
        }

        return $"0,{tenths}%";
    }

    private double Fraction(double part) => Total <= 0 ? 0 : Math.Clamp(part / Total, 0, 1);
}