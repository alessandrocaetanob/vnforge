using System.Globalization;
using VNForge.AI.Cost;

namespace VNForge.App.Controls;

/// <summary>Valores em reais e em dólar no formato pt-BR fixo (<c>R$ 142,56</c>, <c>US$ 26,40</c>), sem depender da cultura nem do ICU da máquina.</summary>
public static class Money
{
    private static readonly NumberFormatInfo PtBr = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Brl(decimal value) => $"R$ {value.ToString("N2", PtBr)}";

    /// <summary>Estimativa em reais: abaixo de um centavo vira "menos de R$ 0,01" em vez de um zero que parece de graça; zero fica R$ 0,00.</summary>
    public static string BrlEstimate(decimal value) =>
        value is > 0m and < 0.01m ? "menos de R$ 0,01" : Brl(Math.Round(value, 2, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Estimativa em reais pela cotação da pessoa. Com a cotação de exemplo (ela ainda não informou a dela) o valor sai com "≈" e a marca
    /// "cotação de exemplo"; valor zero fica sem marca, porque não há o que arredondar.
    /// </summary>
    public static string BrlEstimate(decimal brl, ExchangeRate rate) => WithRateMark(BrlEstimate(brl), brl, rate);

    /// <summary>Põe a marca de cotação de exemplo em um texto de valor já formatado; com a cotação informada, ou sem valor, devolve o texto como veio.</summary>
    public static string WithRateMark(string text, decimal brl, ExchangeRate rate) =>
        rate.IsInformed || brl <= 0m ? text : $"≈ {text} (cotação de exemplo)";

    public static string Usd(decimal value) => $"US$ {value.ToString("N2", PtBr)}";
}