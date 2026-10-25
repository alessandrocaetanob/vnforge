using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>
/// Uma amostra das falas de cada personagem, para a IA sugerir voz e gênero sem ler o jogo inteiro: até <see cref="DefaultLines"/> falas
/// por falante, espalhadas pelo texto (não só o começo), sem repetir a mesma fala.
/// </summary>
public static class SpeakerSamples
{
    public const int DefaultLines = 20;

    /// <summary>Falas por falante, na ordem em que os falantes aparecem. A narração (sem falante) fica de fora.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> From(IEnumerable<TextUnit> units, int maxLines = DefaultLines)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        var bySpeaker = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var speaker = unit.Speaker?.Trim();
            var line = unit.Source.Trim();
            if (string.IsNullOrEmpty(speaker) || line.Length == 0)
            {
                continue;
            }

            if (!bySpeaker.TryGetValue(speaker, out var lines))
            {
                bySpeaker[speaker] = lines = [];
                seen[speaker] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (seen[speaker].Add(line))
            {
                lines.Add(line);
            }
        }

        return bySpeaker.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)Spread(p.Value, maxLines), StringComparer.Ordinal);
    }

    private static List<string> Spread(List<string> lines, int max)
    {
        if (lines.Count <= max)
        {
            return lines;
        }

        var picked = new List<string>(max);
        for (var i = 0; i < max; i++)
        {
            picked.Add(lines[(int)((long)i * lines.Count / max)]);
        }

        return picked;
    }
}
