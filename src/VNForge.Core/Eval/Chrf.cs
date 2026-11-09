namespace VNForge.Core.Eval;

/// <summary>
/// chrF: o quanto uma tradução se parece com a de referência, por n-gramas de caracteres. Segue a definição do sacreBLEU (n-gramas de 1 a 6
/// caracteres, espaços ignorados, beta 2, que pesa mais a revocação), numa escala de 0 a 100. <see cref="Score"/> mede um par de textos;
/// <see cref="Corpus"/> soma as contagens de n-gramas de todos os pares antes de calcular, como o <c>corpus_score</c> do sacreBLEU, o que dá mais peso às linhas longas.
/// </summary>
public static class Chrf
{
    private const int MaxOrder = 6;
    private const double Beta = 2.0;

    /// <summary>A pontuação de <paramref name="hypothesis"/> contra <paramref name="reference"/>; textos iguais valem 100 e um texto vazio contra um cheio vale 0.</summary>
    public static double Score(string? reference, string? hypothesis) => Corpus([(reference, hypothesis)]);

    /// <summary>
    /// O chrF do conjunto: as contagens de n-gramas (da hipótese, da referência e as que casam) são somadas em todos os pares e só então viram precisão, revocação e F.
    /// Uma hipótese vazia entra com os n-gramas da referência e nenhum casando.
    /// </summary>
    public static double Corpus(IEnumerable<(string? Reference, string? Hypothesis)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var hypTotal = new long[MaxOrder];
        var refTotal = new long[MaxOrder];
        var matches = new long[MaxOrder];
        long refChars = 0, hypChars = 0;
        foreach (var (reference, hypothesis) in pairs)
        {
            var r = Strip(reference);
            var h = Strip(hypothesis);
            refChars += r.Length;
            hypChars += h.Length;
            for (var n = 1; n <= MaxOrder; n++)
            {
                var hyp = Count(h, n);
                var rf = Count(r, n);
                hypTotal[n - 1] += Math.Max(0, h.Length - n + 1);
                refTotal[n - 1] += Math.Max(0, r.Length - n + 1);
                foreach (var (gram, count) in hyp)
                {
                    if (rf.TryGetValue(gram, out var other))
                    {
                        matches[n - 1] += Math.Min(count, other);
                    }
                }
            }
        }

        if (refChars == 0 && hypChars == 0)
        {
            return 100.0;
        }

        double precisionSum = 0, recallSum = 0;
        var orders = 0;
        for (var i = 0; i < MaxOrder; i++)
        {
            if (hypTotal[i] == 0 || refTotal[i] == 0)
            {
                continue;
            }

            precisionSum += (double)matches[i] / hypTotal[i];
            recallSum += (double)matches[i] / refTotal[i];
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
