using System.Globalization;

namespace VNForge.Cli;

/// <summary>Números e plurais em pt-BR, do jeito que a saída de texto da CLI os escreve.</summary>
internal static class CliFormat
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Inteiro com separador de milhar: <c>1.254</c>.</summary>
    public static string Number(long value) => value.ToString("N0", PortugueseBrazil);

    /// <summary>Percentual com uma casa e espaço antes do sinal: <c>64,8 %</c>.</summary>
    public static string Percent(double value) => string.Create(PortugueseBrazil, $"{value:0.0} %");

    /// <summary><c>1 linha</c>, <c>2 linhas</c>.</summary>
    public static string Count(long value, string singular, string plural) => $"{Number(value)} {(value == 1 ? singular : plural)}";
}
