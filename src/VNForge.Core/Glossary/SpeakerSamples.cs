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
    /// modo revisão. Fora ficam a narração e os falantes feitos só de sinais; as linhas sem texto contam no total, mas não entram nas falas. A chave ignora a largura dos caracteres, como o
    /// <see cref="CharacterBook"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, SpeakerSheet> Sheets(IEnumerable<TextUnit> units, int longest = DefaultSheetLines)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentOutOfRangeException.ThrowIfLessThan(longest, 1);

        var builders = new Dictionary<string, SheetBuilder>(CharacterBook.NameComparer);
        foreach (var unit in units)
        {
            var speaker = unit.EffectiveSpeaker?.Trim();
            if (string.IsNullOrEmpty(speaker) || SpeakerNames.IsSignsOnly(speaker))
            {
                continue;
            }

            if (!builders.TryGetValue(speaker, out var builder))
            {
                builders[speaker] = builder = new SheetBuilder(longest);
            }

            builder.Total++;
            if (unit.Status == TranslationStatus.Machine)
            {
                builder.AwaitingReview++;
            }

            var line = unit.Source.Trim();
            if (line.Length > 0)
            {
                builder.Offer(line, string.IsNullOrWhiteSpace(unit.Translation) ? null : unit.Translation.Trim());
            }
        }

        return builders.ToDictionary(p => p.Key, p => new SpeakerSheet([.. p.Value.Kept], p.Value.AwaitingReview, p.Value.Total), CharacterBook.NameComparer);
    }

    /// <summary>Guarda só as <c>max</c> falas mais longas de um falante, da maior para a menor: o texto do jogo inteiro não fica na memória.</summary>
    private sealed class SheetBuilder(int max)
    {
        public List<SpokenLine> Kept { get; } = new(max);

        public int Total { get; set; }

        public int AwaitingReview { get; set; }

        public void Offer(string line, string? translation)
        {
            var same = Kept.FindIndex(k => string.Equals(k.Source, line, StringComparison.Ordinal));
            if (same >= 0)
            {
                // A mesma fala repetida: a primeira ocorrência sem tradução fica com a de uma ocorrência traduzida.
                if (Kept[same].Translation is null && translation is not null)
                {
                    Kept[same] = Kept[same] with { Translation = translation };
                }

                return;
            }

            if (Kept.Count == max)
            {
                if (line.Length <= Kept[^1].Source.Length)
                {
                    return;
                }

                Kept.RemoveAt(Kept.Count - 1);
            }

            // Empate pela ordem do texto: entra depois das que têm o mesmo tamanho.
            var at = Kept.FindLastIndex(k => k.Source.Length >= line.Length) + 1;
            Kept.Insert(at, new SpokenLine(line, translation));
        }
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

/// <summary>O que a ficha do personagem mostra do texto do projeto: as falas mais longas, quantas linhas da IA esperam revisão e quantas linhas ele fala no total (as repetidas e as sem texto contam).</summary>
public sealed record SpeakerSheet(IReadOnlyList<SpokenLine> Longest, int AwaitingReview, int Total);
