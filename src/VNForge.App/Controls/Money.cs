using System.Globalization;

namespace VNForge.App.Controls;

/// <summary>Valores em reais e em dólar no formato pt-BR fixo (<c>R$ 142,56</c>, <c>US$ 26,40</c>), sem depender da cultura nem do ICU da máquina.</summary>
public static class Money
{
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Brl(decimal value) => $"R$ {value.ToString("N2", PtBr)}";

    /// <summary>Estimativa em reais: abaixo de um centavo vira "menos de R$ 0,01" em vez de um zero que parece de graça; zero fica R$ 0,00.</summary>
    public static string BrlEstimate(decimal value) =>
        value is > 0m and < 0.01m ? "menos de R$ 0,01" : Brl(Math.Round(value, 2, MidpointRounding.AwayFromZero));

    public static string Usd(decimal value) => $"US$ {value.ToString("N2", PtBr)}";
}