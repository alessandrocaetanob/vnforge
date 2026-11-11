using System.Globalization;
using VNForge.AI.Cost;
using VNForge.Core.Eval;

namespace VNForge.AI.Eval;

/// <summary>Os números do relatório do banco de provas como a pessoa os lê, iguais na CLI e no app: vírgula decimal, reais e minutos.</summary>
public static class EvalText
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary><c>R$ 156,70</c>.</summary>
    public static string Brl(decimal value) => string.Create(Pt, $"R$ {value:N2}");

    /// <summary><c>61,4</c>; o chrF e a nota do juiz levam uma casa decimal.</summary>
    public static string Score(double value) => value.ToString("0.0", Pt);

    /// <summary><c>41 min</c>, <c>1 h 05 min</c> ou <c>menos de 1 min</c>.</summary>
    public static string Duration(TimeSpan time)
    {
        var minutes = (int)Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1)
        {
            return "menos de 1 min";
        }

        return minutes < 60 ? $"{minutes} min" : string.Create(Pt, $"{minutes / 60} h {minutes % 60:00} min");
    }

    /// <summary><c>9 de 12</c>; <paramref name="anyAnswered"/> falso (a rodada às cegas ainda não começou) diz <c>sem rodada</c>, e a configuração que não disputou par diz <c>não rodou</c>.</summary>
    public static string Blind((int Wins, int Total) score, bool anyAnswered)
    {
        if (!anyAnswered)
        {
            return "sem rodada";
        }

        return score.Total == 0 ? "não rodou" : $"{score.Wins} de {score.Total}";
    }

    /// <summary>Quanto a rodada custa em reais, com a cotação informada.</summary>
    public static decimal ToBrl(decimal dollars, ExchangeRate rate) => rate.ToBrl(dollars);

    public static string Number(long value) => value.ToString("N0", Pt);

    public static string CountLines(int lines) => lines == 1 ? "1 linha" : $"{lines.ToString("N0", Pt)} linhas";

    /// <summary>O custo do jogo todo de uma configuração em reais, pelo preço normal e pelo do lote (metade).</summary>
    public static (decimal Normal, decimal Batch) GameCost(EvalConfigReport report, int remainingLines, ExchangeRate rate) =>
        (rate.ToBrl(report.ProjectedDollars(remainingLines)), rate.ToBrl(report.ProjectedBatchDollars(remainingLines)));
}
