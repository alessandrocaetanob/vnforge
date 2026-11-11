using System.Diagnostics.CodeAnalysis;

namespace VNForge.Core.Eval;

/// <summary>Monta os pares da rodada às cegas: a mesma linha traduzida por duas configurações, em ordem e posição sorteadas.</summary>
public static class BlindPairs
{
    /// <summary>
    /// Até <paramref name="count"/> pares. Reveza as combinações de duas configurações, para cada uma disputar pares parecidos, e só usa linhas em que as duas
    /// traduziram e os textos diferem (comparar dois textos iguais não diz nada). Devolve menos pares quando a rodada não tem tantos.
    /// </summary>
    [SuppressMessage("Security Hotspot", "S2245:Using pseudorandom number generators (PRNGs) is security-sensitive", Justification = "O sorteio precisa se repetir com a mesma semente para a rodada ser reproduzida; ele não protege nada.")]
    public static IReadOnlyList<BlindPair> Generate(IReadOnlyList<EvalConfigReport> reports, int count, int seed)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var random = new Random(seed);
        var combos = new List<(EvalConfigReport A, EvalConfigReport B)>();
        for (var i = 0; i < reports.Count; i++)
        {
            for (var j = i + 1; j < reports.Count; j++)
            {
                combos.Add((reports[i], reports[j]));
            }
        }

        if (combos.Count == 0 || count <= 0)
        {
            return [];
        }

        var candidates = combos.Select(c => Shuffle(CommonLines(c.A, c.B), random)).ToList();
        var cursor = new int[combos.Count];
        var pairs = new List<BlindPair>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var empty = 0;
        var k = 0;
        while (pairs.Count < count && empty < combos.Count)
        {
            var c = k++ % combos.Count;
            string? line = null;
            while (cursor[c] < candidates[c].Count && line is null)
            {
                var candidate = candidates[c][cursor[c]++];
                if (used.Add(candidate))
                {
                    line = candidate;
                }
            }

            if (line is null)
            {
                empty++;
                continue;
            }

            empty = 0;
            var (a, b) = combos[c];
            var swap = random.Next(2) == 1;
            pairs.Add(new BlindPair(pairs.Count + 1, line, (swap ? b : a).Config.Id, (swap ? a : b).Config.Id));
        }

        return pairs;
    }

    private static List<string> CommonLines(EvalConfigReport a, EvalConfigReport b)
    {
        var other = b.Lines.Where(l => !l.Refused && l.Translation.Length > 0).ToDictionary(l => l.LineId, l => l.Translation, StringComparer.Ordinal);
        return [.. a.Lines.Where(l => !l.Refused && l.Translation.Length > 0 && other.TryGetValue(l.LineId, out var text) && !string.Equals(text, l.Translation, StringComparison.Ordinal)).Select(l => l.LineId)];
    }

    private static List<string> Shuffle(List<string> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }
}
