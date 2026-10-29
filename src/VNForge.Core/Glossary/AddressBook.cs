namespace VNForge.Core.Glossary;

/// <summary>Os tratamentos de <c>address-terms.json</c> indexados pelo par, para achar como um falante chama os outros.</summary>
public sealed class AddressBook
{
    private readonly Dictionary<(string, string), AddressTerm> _byPair = [];

    public AddressBook(IEnumerable<AddressTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        foreach (var term in terms)
        {
            _byPair.TryAdd(PairKey(term.From, term.To), term);
        }
    }

    /// <summary>Os tratamentos na ordem do arquivo.</summary>
    public IReadOnlyCollection<AddressTerm> Terms => _byPair.Values;

    /// <summary>Como <paramref name="from"/> chama <paramref name="to"/>; nulo quando o par não está no livro. Os nomes seguem a comparação de <see cref="CharacterBook.NameComparer"/>.</summary>
    public AddressTerm? Find(string? from, string? to) =>
        string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) ? null : _byPair.GetValueOrDefault(PairKey(from, to));

    /// <summary>Os tratamentos que <paramref name="from"/> usa, na ordem do arquivo.</summary>
    public IReadOnlyList<AddressTerm> From(string? from) =>
        string.IsNullOrWhiteSpace(from) ? [] : [.. _byPair.Values.Where(t => CharacterBook.NameComparer.Equals(t.From, from))];

    internal static (string, string) PairKey(string from, string to) => (Normalize(from), Normalize(to));

    private static string Normalize(string name) => name.Trim().Normalize(System.Text.NormalizationForm.FormKC);
}
