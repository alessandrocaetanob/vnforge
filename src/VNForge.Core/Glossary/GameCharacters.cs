using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>
/// A lista de personagens que o próprio jogo traz: as unidades <see cref="TextUnitKind.Name"/> (no YU-RIS, uma por chamada de <c>ES.CHAR.NAME</c>).
/// É a proposta inicial de Glossário &gt; Personagens, com o nome original exatamente como o jogo o escreve.
/// </summary>
public static class GameCharacters
{
    /// <summary>
    /// Os nomes do jogo, um por original (sem espaços nas pontas, na ordem da primeira aparição). O alvo é a tradução da primeira unidade do nome que
    /// tem uma (<see cref="TextUnit.HasTranslation"/>) e fica vazio quando nenhuma tem.
    /// </summary>
    public static IReadOnlyList<CharacterEntry> Names(IEnumerable<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return
        [
            .. units
                .Where(u => u.Kind == TextUnitKind.Name && !string.IsNullOrWhiteSpace(u.Source))
                .GroupBy(u => u.Source.Trim(), StringComparer.Ordinal)
                .Select(g => new CharacterEntry(g.Key, g.FirstOrDefault(u => u.HasTranslation)?.Translation ?? string.Empty)),
        ];
    }

    /// <summary>Os <paramref name="names"/> que ainda não são personagem de <paramref name="existing"/>, achados por <see cref="CharacterBook.Find"/>.</summary>
    public static IReadOnlyList<CharacterEntry> Missing(IEnumerable<CharacterEntry> names, IEnumerable<CharacterEntry> existing)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(existing);
        var known = existing.ToList();
        var missing = new List<CharacterEntry>();
        foreach (var name in names)
        {
            // O livro é imutável: refazê-lo a cada nome novo cobre dois nomes que a comparação trata como o mesmo.
            if (new CharacterBook(known).Find(name.Source) is null)
            {
                known.Add(name);
                missing.Add(name);
            }
        }

        return missing;
    }
}
