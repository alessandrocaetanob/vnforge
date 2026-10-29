using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>
/// Uma amostra das falas de cada personagem, para a IA sugerir voz e gênero sem ler o jogo inteiro: até <see cref="DefaultLines"/> falas
/// por falante, espalhadas pelo texto (não só o começo), sem repetir a mesma fala.
/// </summary>
public static class SpeakerSamples
{
    public const int DefaultLines = 20;

    /// <summary>Menos falas que isso e o falante provavelmente é um rótulo avulso, não uma pessoa: não vale pedir voz e gênero à IA.</summary>
    public const int DefaultMinLines = 3;

    /// <summary>Quantas falas a ficha do personagem mostra.</summary>
    public const int DefaultSheetLines = 5;

    /// <summary>
    /// Falas por falante, na ordem em que os falantes aparecem. A narração (sem falante) fica de fora, assim como os falantes feitos só de
    /// sinais (<see cref="SpeakerNames.IsSignsOnly"/>, como <c>？？？</c>) e os que têm menos de <paramref name="minLines"/> falas com texto
    /// (a mesma fala repetida conta cada vez).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> From(
        IEnumerable<TextUnit> units, int maxLines = DefaultLines, int minLines = DefaultMinLines)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minLines, 1);

        var bySpeaker = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var spoken = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var speaker = unit.Speaker?.Trim();
            var line = unit.Source.Trim();
            if (string.IsNullOrEmpty(speaker) || line.Length == 0 || SpeakerNames.IsSignsOnly(speaker))
            {
                continue;
            }

            if (!bySpeaker.TryGetValue(speaker, out var lines))
            {
                bySpeaker[speaker] = lines = [];
                seen[speaker] = new HashSet<string>(StringComparer.Ordinal);
            }

            spoken[speaker] = spoken.GetValueOrDefault(speaker) + 1;
            if (seen[speaker].Add(line))
            {
                lines.Add(line);
            }
        }

        return bySpeaker
            .Where(p => spoken[p.Key] >= minLines)
            .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)Spread(p.Value, maxLines), StringComparer.Ordinal);
    }

    /// <summary>
    /// A ficha de cada personagem (#469): as <paramref name="longest"/> falas mais longas no original, da maior para a menor (empate pela ordem do texto, sem repetir
    /// a mesma fala), e quantas linhas da IA ainda esperam revisão. Vale o falante que quem traduz escolheu (<see cref="TextUnit.EffectiveSpeaker"/>), o mesmo do
    /// modo revisão. Fora ficam a narração, os falantes feitos só de sinais e as linhas sem texto. A chave ignora a largura dos caracteres, como o
    /// <see cref="CharacterBook"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, SpeakerSheet> Sheets(IEnumerable<TextUnit> units, int longest = DefaultSheetLines)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(longest, 1);

        var lines = new Dictionary<string, List<SpokenLine>>(CharacterBook.NameComparer);
        var at = new Dictionary<string, Dictionary<string, int>>(CharacterBook.NameComparer);
        var awaiting = new Dictionary<string, int>(CharacterBook.NameComparer);
        foreach (var unit in units)
        {
            var speaker = unit.EffectiveSpeaker?.Trim();
            var line = unit.Source.Trim();
            if (string.IsNullOrEmpty(speaker) || line.Length == 0 || SpeakerNames.IsSignsOnly(speaker))
            {
                continue;
            }

            if (!lines.TryGetValue(speaker, out var spoken))
            {
                lines[speaker] = spoken = [];
                at[speaker] = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            if (unit.Status == TranslationStatus.Machine)
            {
                awaiting[speaker] = awaiting.GetValueOrDefault(speaker) + 1;
            }

            var translation = string.IsNullOrWhiteSpace(unit.Translation) ? null : unit.Translation.Trim();
            if (!at[speaker].TryGetValue(line, out var index))
            {
                at[speaker][line] = spoken.Count;
                spoken.Add(new SpokenLine(line, translation));
            }
            else if (spoken[index].Translation is null && translation is not null)
            {
                // A mesma fala repetida: a primeira ocorrência sem tradução fica com a de uma ocorrência traduzida.
                spoken[index] = spoken[index] with { Translation = translation };
            }
        }

        return lines.ToDictionary(
            p => p.Key,
            p => new SpeakerSheet([.. p.Value.OrderByDescending(l => l.Source.Length).Take(longest)], awaiting.GetValueOrDefault(p.Key)),
            CharacterBook.NameComparer);
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

/// <summary>Uma fala do personagem no original e, quando já existe, na tradução (nula se estiver vazia).</summary>
public sealed record SpokenLine(string Source, string? Translation);

/// <summary>O que a ficha do personagem mostra do texto do projeto: as falas mais longas e quantas linhas da IA esperam revisão.</summary>
public sealed record SpeakerSheet(IReadOnlyList<SpokenLine> Longest, int AwaitingReview);
