namespace VNForge.Core.Glossary;

/// <summary>
/// Os personagens de <c>characters.json</c> indexados pelo nome original, para achar a entrada de um <see cref="Text.TextUnit.Speaker"/>.
/// </summary>
public sealed class CharacterBook
{
    /// <summary>Compara nomes ignorando espaços nas pontas e a largura (NFKC): <c>学生Ｃ</c> do jogo e <c>学生C</c> digitado são o mesmo personagem.</summary>
    public static IEqualityComparer<string> NameComparer { get; } = new WidthInsensitiveComparer();

    private readonly Dictionary<string, CharacterEntry> _bySource = new(NameComparer);

    public CharacterBook(IEnumerable<CharacterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            _bySource.TryAdd(entry.Source, entry);
        }
    }

    /// <summary>
    /// Procura o personagem pelo falante extraído. A comparação é ordinal, ignorando só espaços nas pontas
    /// (inclusive o ideográfico U+3000) e a largura; nulo, vazio ou desconhecido devolvem <c>null</c>.
    /// </summary>
    public CharacterEntry? Find(string? speaker) =>
        string.IsNullOrWhiteSpace(speaker) ? null : _bySource.GetValueOrDefault(speaker);

    private sealed class WidthInsensitiveComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(Key(x), Key(y), StringComparison.Ordinal);

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Key(obj)!);

        private static string? Key(string? name) => name?.Trim().Normalize(System.Text.NormalizationForm.FormKC);
    }
}
