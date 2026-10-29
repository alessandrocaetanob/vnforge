namespace VNForge.Core.Glossary;

/// <summary>Os tratamentos de <c>address-terms.json</c> indexados pelo par e pelo falante, para achar como um falante chama os outros.</summary>
public sealed class AddressBook
{
    private readonly Dictionary<(string, string), AddressTerm> _byPair = [];
    private readonly Dictionary<string, List<AddressTerm>> _byFrom = new(StringComparer.Ordinal);

    public AddressBook(IEnumerable<AddressTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        foreach (var term in terms)
        {
            if (!_byPair.TryAdd(PairKey(term.From, term.To), term))
            {
                continue;
            }

            var from = Normalize(term.From);
            if (!_byFrom.TryGetValue(from, out var list))
            {
                _byFrom[from] = list = [];
            }

            list.Add(term);
        }
    }

    /// <summary>Os tratamentos na ordem do arquivo.</summary>
    public IReadOnlyCollection<AddressTerm> Terms => _byPair.Values;

    /// <summary>Verdadeiro quando o livro não tem nenhum tratamento.</summary>
    public bool IsEmpty => _byPair.Count == 0;

    /// <summary>Como <paramref name="from"/> chama <paramref name="to"/>; nulo quando o par não está no livro. Os nomes seguem a comparação de <see cref="CharacterBook.NameComparer"/>.</summary>
    public AddressTerm? Find(string? from, string? to) =>
        string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) ? null : _byPair.GetValueOrDefault(PairKey(from, to));

    /// <summary>Os tratamentos que <paramref name="from"/> usa, na ordem do arquivo; uma lista vazia (sempre a mesma) quando ele não tem nenhum.</summary>
    public IReadOnlyList<AddressTerm> From(string? from) =>
        string.IsNullOrWhiteSpace(from) || !_byFrom.TryGetValue(Normalize(from), out var list) ? [] : list;

    internal static (string, string) PairKey(string from, string to) => (Normalize(from), Normalize(to));

    private static string Normalize(string name) => name.Trim().Normalize(System.Text.NormalizationForm.FormKC);
}
