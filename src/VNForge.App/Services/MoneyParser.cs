using System.Globalization;

namespace VNForge.App.Services;

/// <summary>
/// Lê o valor em reais que a pessoa digita: <c>5,40</c>, <c>1.250,00</c>, <c>R$ 200</c>. Sem vírgula, um ponto seguido de um ou dois dígitos
/// vale como vírgula decimal (<c>5.40</c>), e um ponto seguido de três dígitos separa milhares (<c>1.250</c>). Não depende da cultura da máquina.
/// </summary>
public static class MoneyParser
{
    private static readonly NumberFormatInfo PtBr = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };

    private static readonly NumberFormatInfo Invariant = NumberFormatInfo.InvariantInfo;

    /// <summary>Texto vazio não é erro de valor: quem chama decide se vazio significa "sem limite".</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        var cleaned = (text ?? string.Empty).Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (cleaned.Length == 0 || cleaned.Any(c => !char.IsAsciiDigit(c) && c is not (',' or '.')))
        {
            return false;
        }

        if (cleaned.Contains(',', StringComparison.Ordinal))
        {
            return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out value) && value >= 0m;
        }

        var lastDot = cleaned.LastIndexOf('.');
        var afterDot = lastDot < 0 ? 0 : cleaned.Length - lastDot - 1;
        var decimalPoint = cleaned.Count(c => c == '.') == 1 && afterDot is >= 1 and <= 2;
        return decimalPoint
            ? decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, Invariant, out value) && value >= 0m
            : decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out value) && value >= 0m;
    }

    /// <summary>O valor como a pessoa o digita, sem o "R$": <c>5,40</c>, <c>1.250,00</c>.</summary>
    public static string Format(decimal value) => value.ToString("N2", PtBr);
}
