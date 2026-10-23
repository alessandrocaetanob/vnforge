namespace VNForge.Core.Glossary;

/// <summary>
/// Os personagens de <c>characters.json</c> indexados pelo nome original, para achar a entrada de um <see cref="Text.TextUnit.Speaker"/>.
/// </summary>
public sealed class CharacterBook
{
    private readonly Dictionary<string, CharacterEntry> _bySource = new(StringComparer.Ordinal);

    public CharacterBook(IEnumerable<CharacterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            _bySource.TryAdd(entry.Source.Trim(), entry);
        }
    }

    /// <summary>
    /// Procura o personagem pelo falante extraído. A comparação é ordinal, ignorando só espaços nas pontas
    /// (inclusive o ideográfico U+3000); nulo, vazio ou desconhecido devolvem <c>null</c>.
    /// </summary>
    public CharacterEntry? Find(string? speaker)
    {
        var name = speaker?.Trim();
        return string.IsNullOrEmpty(name) ? null : _bySource.GetValueOrDefault(name);
    }
}
