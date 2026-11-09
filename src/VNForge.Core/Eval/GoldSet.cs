using VNForge.Core.Text;

namespace VNForge.Core.Eval;

/// <summary>
/// O conjunto ouro do banco de provas: as linhas que a pessoa aprovou. Cada configuração traduz o original dessas linhas e o resultado é comparado com a
/// tradução aprovada. O sorteio é determinístico para uma mesma semente, para a rodada poder ser repetida com a mesma amostra.
/// </summary>
public static class GoldSet
{
    private const int NeighborMaxLength = 70;

    /// <summary>As linhas aprovadas com original e tradução, na ordem dos scripts, cada uma com a fala de antes e a de depois.</summary>
    public static IReadOnlyList<GoldLine> From(IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var gold = new List<GoldLine>();
        foreach (var (key, units) in texts)
        {
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit.Status != TranslationStatus.Approved || string.IsNullOrWhiteSpace(unit.Source) || string.IsNullOrWhiteSpace(unit.Translation))
                {
                    continue;
                }

                gold.Add(new GoldLine(unit.Id, key, unit.Speaker, unit.Source, unit.Translation, Neighbor(units, i - 1), Neighbor(units, i + 1)));
            }
        }

        return gold;
    }

    /// <summary>Quantas linhas ainda não têm tradução: é para elas que o custo e o tempo do jogo todo valem.</summary>
    public static int RemainingLines(IEnumerable<KeyValuePair<string, IReadOnlyList<TextUnit>>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return texts.Sum(script => script.Value.Count(u => u.Status == TranslationStatus.Untranslated && !string.IsNullOrWhiteSpace(u.Source)));
    }

    /// <summary>Sorteia <paramref name="count"/> linhas (todas, se o conjunto for menor) e as devolve na ordem original.</summary>
    public static IReadOnlyList<GoldLine> Sample(IReadOnlyList<GoldLine> gold, int count, int seed)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count >= gold.Count)
        {
            return gold;
        }

        var random = new Random(seed);
        var indexes = Enumerable.Range(0, gold.Count).ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = random.Next(i, indexes.Length);
            (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
        }

        return [.. indexes.Take(count).Order().Select(i => gold[i])];
    }

    private static string Neighbor(IReadOnlyList<TextUnit> units, int index)
    {
        if (index < 0 || index >= units.Count)
        {
            return string.Empty;
        }

        var unit = units[index];
        var text = string.IsNullOrWhiteSpace(unit.Translation) ? unit.Source : unit.Translation;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.ReplaceLineEndings(" ").Trim();
        if (text.Length > NeighborMaxLength)
        {
            text = text[..NeighborMaxLength].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(unit.Speaker) ? $"\"{text}\"" : $"{unit.Speaker}, \"{text}\"";
    }
}
