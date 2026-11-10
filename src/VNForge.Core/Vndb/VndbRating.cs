using System.Globalization;

namespace VNForge.Core.Vndb;

/// <summary>A nota do VNDB como as telas a escrevem.</summary>
public static class VndbRating
{
    // Formato pt-BR fixo: não depende do ICU da máquina nem da cultura do usuário.
    private static readonly NumberFormatInfo PtBr = new() { NumberDecimalSeparator = "," };

    /// <summary>
    /// "nota 7,9" para a nota <c>rating</c> do VNDB (de 10 a 100, a média bayesiana), arredondada a uma casa na escala de 1 a 10; nulo quando ninguém votou.
    /// </summary>
    public static string? Label(double? rating)
    {
        if (rating is not { } value || value <= 0 || double.IsNaN(value))
        {
            return null;
        }

        var outOfTen = Math.Round(value / 10, 1, MidpointRounding.AwayFromZero);
        return "nota " + outOfTen.ToString("0.#", PtBr);
    }

    /// <summary>"7,9 de 10" para a nota <c>rating</c> do VNDB; nulo quando ninguém votou.</summary>
    public static string? Score(double? rating) => Label(rating) is { } label ? label["nota ".Length..] + " de 10" : null;
}
