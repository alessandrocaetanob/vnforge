namespace VNForge.Core.Glossary;

/// <summary>
/// Segue o nome de um personagem nos tratamentos (#514): <see cref="AddressTerm.From"/> e <see cref="AddressTerm.To"/> guardam o nome original, então renomear
/// o personagem em <c>characters.json</c> sem passar por aqui deixa o par com o nome antigo, que ninguém mais encontra.
/// </summary>
public static class AddressTermRenamer
{
    /// <summary>O nome que <paramref name="name"/> passa a ter; o próprio nome quando <paramref name="renames"/> não o cita. Os nomes seguem <see cref="CharacterBook.NameComparer"/>.</summary>
    public static string NameAfter(string name, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Renamer(renames)(name);
    }

    /// <summary>Como <see cref="NameAfter"/>, para quem troca muitos nomes: monta o índice dos renomes uma vez só.</summary>
    public static Func<string, string> Renamer(IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(renames);
        var index = Index(renames);
        return name => index.GetValueOrDefault(name, name);
    }

    /// <summary>
    /// Troca os nomes dos dois lados de cada par. Os renomes valem todos ao mesmo tempo (dois personagens podem trocar de nome entre si). Mantém a ordem do arquivo;
    /// o par que ficaria com o mesmo falante nos dois lados some, e quando o par renomeado cai em um que já existe, o que já existia fica e o renomeado some.
    /// </summary>
    /// <param name="renames">Nome atual para nome novo.</param>
    public static IReadOnlyList<AddressTerm> Apply(IReadOnlyList<AddressTerm> terms, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(renames);
        return renames.Count == 0 ? terms : [.. ApplyEach(terms, renames).OfType<AddressTerm>()];
    }

    /// <summary>
    /// As mesmas regras de <see cref="Apply"/>, mas devolve uma posição para cada par recebido: o par renomeado, o mesmo objeto quando nenhum renome o citou,
    /// ou nulo quando o par some. Serve a quem guarda o par em outra estrutura (as linhas da tabela do app) e precisa saber qual linha ficou.
    /// </summary>
    public static IReadOnlyList<AddressTerm?> ApplyEach(IReadOnlyList<AddressTerm> terms, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(renames);
        var index = Index(renames);
        var moved = terms.Select(t => (Term: t, Renamed: Rename(t, index))).ToList();
        var taken = moved.Where(m => !m.Renamed.Changed).Select(m => AddressBook.PairKey(m.Term.From, m.Term.To)).ToHashSet();
        var result = new List<AddressTerm?>(terms.Count);
        foreach (var (term, renamed) in moved)
        {
            result.Add(renamed.Changed ? Kept(renamed.Term, taken) : term);
        }

        return result;
    }

    private static AddressTerm? Kept(AddressTerm renamed, HashSet<(string, string)> taken) =>
        !CharacterBook.NameComparer.Equals(renamed.From, renamed.To) && taken.Add(AddressBook.PairKey(renamed.From, renamed.To)) ? renamed : null;

    private static (AddressTerm Term, bool Changed) Rename(AddressTerm term, Dictionary<string, string> index)
    {
        var hasFrom = index.TryGetValue(term.From, out var from);
        var hasTo = index.TryGetValue(term.To, out var to);
        return hasFrom || hasTo ? (term with { From = from ?? term.From, To = to ?? term.To }, true) : (term, false);
    }

    private static Dictionary<string, string> Index(IReadOnlyDictionary<string, string> renames)
    {
        var index = new Dictionary<string, string>(CharacterBook.NameComparer);
        foreach (var (oldName, newName) in renames)
        {
            index[oldName] = newName.Trim();
        }

        return index;
    }
}
