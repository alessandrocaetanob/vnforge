using System.Globalization;

namespace VNForge.Core.Eval;

/// <summary>
/// A leitura da rodada em uma frase e a configuração que o relatório sugere para o M4. A sugestão sai de número: a configuração com o maior chrF é a
/// referência, e a sugerida é a mais barata das que chegam a 95% dela. Se nenhuma é mais barata e chega tão perto, a sugerida é a própria referência.
/// A pessoa decide; a rodada às cegas pesa na frase, e a escolha final é dela.
/// </summary>
public static class EvalReading
{
    /// <summary>Até onde uma configuração mais barata pode ficar abaixo do chrF da referência para ainda ser sugerida.</summary>
    public const double CloseEnough = 0.95;

    public static EvalConfigReport? Reference(EvalRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        return round.Reports.Where(r => r.Lines.Count > 0).OrderByDescending(r => r.MeanChrf).ThenBy(r => r.Config.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    public static EvalConfigReport? Recommended(EvalRound round)
    {
        var reference = Reference(round);
        if (reference is null)
        {
            return null;
        }

        var cheaper = round.Reports
            .Where(r => r != reference && r.Lines.Count > 0 && r.MeanChrf >= reference.MeanChrf * CloseEnough && r.Dollars < reference.Dollars)
            .OrderBy(r => r.Dollars)
            .ThenBy(r => r.Config.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return cheaper ?? reference;
    }

    /// <summary>A frase de "Leitura da rodada"; vazia quando a rodada não tem relatório.</summary>
    public static string Sentence(EvalRound round)
    {
        var reference = Reference(round);
        var chosen = Recommended(round);
        if (reference is null || chosen is null)
        {
            return string.Empty;
        }

        if (chosen == reference)
        {
            return $"{reference.Config.Id} tem o maior chrF, {reference.MeanChrf.ToString("0.0", Pt)}, e nenhuma configuração mais barata chega a {(int)(CloseEnough * 100)}% dele.";
        }

        var percent = (int)Math.Round(chosen.MeanChrf / reference.MeanChrf * 100, MidpointRounding.AwayFromZero);
        var text = $"{chosen.Config.Id} chega a {percent}% do chrF de {reference.Config.Id}{PriceClause(chosen, reference)}";
        var (wins, total) = round.HeadToHead(chosen.Config.Id, reference.Config.Id);
        var pairNoun = total == 1 ? "par" : "pares";
        text += total > 0
            ? $", e você preferiu {chosen.Config.Id} em {wins} de {total} {pairNoun} contra {reference.Config.Id}."
            : $". A rodada às cegas ainda não comparou {chosen.Config.Id} com {reference.Config.Id}.";

        var worst = round.Reports.Where(r => r != reference && r.Lines.Count > 0).OrderBy(r => r.MeanChrf).FirstOrDefault();
        if (worst is not null && worst.MeanChrf < reference.MeanChrf * CloseEnough)
        {
            var why = Difference(worst.Config, reference.Config);
            text += $" A pior diferença aparece em {worst.Config.Id}{(why.Length > 0 ? $", {why}" : string.Empty)}.";
        }

        return text;
    }

    private static string PriceClause(EvalConfigReport chosen, EvalConfigReport reference)
    {
        if (reference.Dollars <= 0m)
        {
            return string.Empty;
        }

        var ratio = (double)(chosen.Dollars / reference.Dollars);
        return ratio is >= 0.45 and <= 0.55
            ? " por metade do preço"
            : $" por {(int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero)}% do preço";
    }

    private static string Difference(EvalConfig config, EvalConfig reference)
    {
        if (reference.WithBible && !config.WithBible)
        {
            return "sem a bíblia";
        }

        if (config.ContextLines < reference.ContextLines)
        {
            return $"com {config.ContextText}";
        }

        if (!string.Equals(config.Model, reference.Model, StringComparison.Ordinal))
        {
            return $"no {config.ModelName}";
        }

        return string.Equals(config.Effort, reference.Effort, StringComparison.OrdinalIgnoreCase) ? string.Empty : $"com {config.EffortText}";
    }

    private static CultureInfo Pt { get; } = CultureInfo.GetCultureInfo("pt-BR");
}
