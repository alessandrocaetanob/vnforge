using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>Uma fala em que o falante provavelmente chama alguém, com quem falou antes dele (quase sempre a pessoa a quem ele responde).</summary>
/// <param name="Text">O original da fala.</param>
/// <param name="ReplyingTo">O personagem (pelo nome de <see cref="CharacterEntry.Source"/>) que falou antes de outro personagem; nulo quando não há.</param>
public sealed record AddressLine(string Text, string? ReplyingTo);

/// <summary>
/// As falas em que cada personagem chama alguém (um nome de outro personagem ou um honorífico como ちゃん e 先輩), para a IA preencher o
/// tratamento entre eles (#470) sem ler o jogo inteiro: até <see cref="DefaultLines"/> por falante, espalhadas pelo texto, sem repetir a fala.
/// </summary>
public static class AddressSamples
{
    public const int DefaultLines = 12;

    private const int MinNameLength = 2;

    private static readonly string[] Markers = ["先輩", "せんぱい", "ちゃん", "くん", "君", "さん", "さま", "様", "先生", "お兄", "お姉"];

    /// <summary>
    /// Falas por falante, na ordem em que os falantes aparecem. Só entram os falantes de <paramref name="characters"/> e as falas que citam o nome
    /// de <em>outro</em> personagem ou trazem um honorífico; quem não tem nenhuma fica de fora. As <paramref name="units"/> vêm na ordem do jogo.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<AddressLine>> From(
        IEnumerable<TextUnit> units, IEnumerable<CharacterEntry> characters, int maxLines = DefaultLines)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        var people = characters.ToList();
        var book = new CharacterBook(people);
        var tokens = book.Entries.ToDictionary(c => c.Source, NameTokens, StringComparer.Ordinal);

        var bySpeaker = new Dictionary<string, List<AddressLine>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string? previous = null;
        foreach (var unit in units)
        {
            var text = unit.Source.Trim();
            if (SpeakerNames.IsSignsOnly(unit.EffectiveSpeaker) || text.Length == 0 || book.Find(unit.EffectiveSpeaker) is not { } speaker)
            {
                previous = null;
                continue;
            }

            var replyingTo = previous is not null && !string.Equals(previous, speaker.Source, StringComparison.Ordinal) ? previous : null;
            previous = speaker.Source;
            if (!Addresses(text, speaker.Source, tokens))
            {
                continue;
            }

            if (!bySpeaker.TryGetValue(speaker.Source, out var lines))
            {
                bySpeaker[speaker.Source] = lines = [];
                seen[speaker.Source] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (seen[speaker.Source].Add(text))
            {
                lines.Add(new AddressLine(text, replyingTo));
            }
        }

        return bySpeaker.ToDictionary(p => p.Key, p => (IReadOnlyList<AddressLine>)Spread(p.Value, maxLines), StringComparer.Ordinal);
    }

    private static bool Addresses(string text, string speaker, Dictionary<string, string[]> tokens) =>
        Markers.Any(m => text.Contains(m, StringComparison.Ordinal))
        || tokens.Any(p => !string.Equals(p.Key, speaker, StringComparison.Ordinal) && p.Value.Any(t => text.Contains(t, StringComparison.Ordinal)));

    /// <summary>O nome inteiro e cada pedaço separado por espaço (inclusive o ideográfico): "佐藤 美咲" vale como "佐藤" e "美咲".</summary>
    private static string[] NameTokens(CharacterEntry character)
    {
        var name = character.Source.Trim();
        var parts = name.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries);
        return [.. parts.Append(name).Where(t => t.Length >= MinNameLength).Distinct(StringComparer.Ordinal)];
    }

    private static List<AddressLine> Spread(List<AddressLine> lines, int max)
    {
        if (lines.Count <= max)
        {
            return lines;
        }

        var picked = new List<AddressLine>(max);
        for (var i = 0; i < max; i++)
        {
            picked.Add(lines[(int)((long)i * lines.Count / max)]);
        }

        return picked;
    }
}
