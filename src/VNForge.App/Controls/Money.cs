using System.Globalization;

namespace VNForge.App.Controls;

/// <summary>Valores em reais e em dólar no formato pt-BR fixo (<c>R$ 142,56</c>, <c>US$ 26,40</c>), sem depender da cultura nem do ICU da máquina.</summary>
public static class Money
{
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Brl(decimal value) => $"R$ {value.ToString("N2", PtBr)}";

    public static string Usd(decimal value) => $"US$ {value.ToString("N2", PtBr)}";
}