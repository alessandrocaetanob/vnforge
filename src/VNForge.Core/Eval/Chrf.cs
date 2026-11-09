namespace VNForge.Core.Eval;

/// <summary>
/// chrF: o quanto uma tradução se parece com a de referência, por n-gramas de caracteres. Segue a definição do sacreBLEU (n-gramas de 1 a 6
/// caracteres, espaços ignorados, beta 2, que pesa mais a revocação), numa escala de 0 a 100.
/// </summary>
public static class Chrf
{
    private const int MaxOrder = 6;
    private const double Beta = 2.0;

    /// <summary>A pontuação de <paramref name="hypothesis"/> contra <paramref name="reference"/>; textos iguais valem 100 e um texto vazio contra um cheio vale 0.</summary>
    public static double Score(string? reference, string? hypothesis)
    {
        var refChars = Strip(reference);
        var hypChars = Strip(hypothesis);
        if (refChars.Length == 0 && hypChars.Length == 0)
        {
            return 100.0;
        }

        if (refChars.Length == 0 || hypChars.Length == 0)
        {
            return 0.0;
        }

        double precisionSum = 0, recallSum = 0;
        var orders = 0;
        for (var n = 1; n <= MaxOrder; n++)
        {
            var hyp = Count(hypChars, n);
            var rf = Count(refChars, n);
            var hypTotal = Math.Max(0, hypChars.Length - n + 1);
            var refTotal = Math.Max(0, refChars.Length - n + 1);
            if (hypTotal == 0 || refTotal == 0)
            {
                continue;
            }

            var matches = 0;
            foreach (var (gram, count) in hyp)
            {
                if (rf.TryGetValue(gram, out var other))
                {
                    matches += Math.Min(count, other);
                }
            }

            precisionSum += (double)matches / hypTotal;
            recallSum += (double)matches / refTotal;
            orders++;
        }

        if (orders == 0)
        {
            return 0.0;
        }

        var precision = precisionSum / orders;
        var recall = recallSum / orders;
        if (precision + recall == 0)
        {
            return 0.0;
        }

        var beta2 = Beta * Beta;
        return 100.0 * (1 + beta2) * precision * recall / (beta2 * precision + recall);
    }

    private static string Strip(string? text) =>
        text is null ? string.Empty : string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static Dictionary<string, int> Count(string text, int n)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + n <= text.Length; i++)
        {
            var gram = text.Substring(i, n);
            counts[gram] = counts.GetValueOrDefault(gram) + 1;
        }

        return counts;
    }
}
