using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>De quem é a tag que um termo do filtro procura.</summary>
public enum LibraryTagScope
{
    /// <summary>Tag própria ou tag do VNDB com esse nome (ou id, no caso do VNDB).</summary>
    Any,

    /// <summary>Só tag própria (<c>minha:favorito</c>).</summary>
    Own,

    /// <summary>Só tag do VNDB (<c>vndb:drama</c> ou <c>vndb:g1</c>).</summary>
    Vndb,
}

/// <summary>Um termo do filtro: o nome (ou o id do VNDB) em minúsculas e de quem é a tag.</summary>
public sealed record LibraryTagTerm(LibraryTagScope Scope, string Name)
{
    public const string OwnPrefix = "minha:";
    public const string VndbPrefix = "vndb:";

    /// <exception cref="LibraryException">O termo está vazio.</exception>
    public static LibraryTagTerm Parse(string? text)
    {
        var value = (text ?? string.Empty).TrimStart();
        var scope = LibraryTagScope.Any;
        if (value.StartsWith(OwnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            scope = LibraryTagScope.Own;
            value = value[OwnPrefix.Length..];
        }
        else if (value.StartsWith(VndbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            scope = LibraryTagScope.Vndb;
            value = value[VndbPrefix.Length..];
        }

        var name = UserTagName.Collapse(value);
        return name.Length == 0
            ? throw new LibraryException("Uma tag do filtro está vazia.")
            : new LibraryTagTerm(scope, name);
    }

    /// <summary>O termo como se escreve na linha de comando e no arquivo.</summary>
    public override string ToString() => Scope switch
    {
        LibraryTagScope.Own => OwnPrefix + Name,
        LibraryTagScope.Vndb => VndbPrefix + Name,
        _ => Name,
    };

    /// <summary>A tag do VNDB combina por id (<c>g1</c>) ou por nome.</summary>
    internal bool Matches(LibraryTag tag) =>
        string.Equals(tag.Id, Name, StringComparison.OrdinalIgnoreCase) || UserTagName.Collapse(tag.Name) == Name;
}

/// <summary>
/// O filtro da Biblioteca por tag, juntando as tags próprias e as do VNDB. <paramref name="Include"/> são os termos que o jogo precisa ter (todos, ou qualquer um com
/// <paramref name="Any"/>); <paramref name="Exclude"/> são os que ele não pode ter. Um termo é <c>nome</c> (própria ou VNDB), <c>minha:nome</c> ou <c>vndb:nome-ou-id</c>.
/// As tags do VNDB entram só se a <see cref="LibraryPrivacy"/> as mostra: um filtro por tag de spoiler escondida não acha nada, para não revelar o que a privacidade esconde.
/// </summary>
public sealed record LibraryTagFilter(IReadOnlyList<string> Include, bool Any, IReadOnlyList<string> Exclude)
{
    public static LibraryTagFilter Empty { get; } = new([], false, []);

    [JsonIgnore]
    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0;

    /// <summary>Monta o filtro com os termos normalizados e sem repetição.</summary>
    /// <exception cref="LibraryException">Um termo está vazio.</exception>
    public static LibraryTagFilter Create(IEnumerable<string>? include, bool any, IEnumerable<string>? exclude) =>
        new(Terms(include), any, Terms(exclude));

    public bool Matches(LibraryGame game, UserTags tags, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(privacy);
        if (IsEmpty)
        {
            return true;
        }

        var own = tags.TagsOf(game.Id);
        var vndb = LibraryVisibility.VisibleTags(game, privacy);
        bool Has(string text)
        {
            var term = LibraryTagTerm.Parse(text);
            return (term.Scope != LibraryTagScope.Vndb && own.Contains(term.Name, StringComparer.Ordinal))
                || (term.Scope != LibraryTagScope.Own && vndb.Any(term.Matches));
        }

        if (Exclude.Any(Has))
        {
            return false;
        }

        if (Include.Count == 0)
        {
            return true;
        }

        return Any ? Include.Any(Has) : Include.All(Has);
    }

    public IReadOnlyList<LibraryGame> Apply(IEnumerable<LibraryGame> games, UserTags tags, LibraryPrivacy privacy)
    {
        ArgumentNullException.ThrowIfNull(games);
        return [.. games.Where(g => Matches(g, tags, privacy))];
    }

    /// <summary>O filtro com a tag própria <paramref name="from"/> trocada por <paramref name="to"/>. Termos do VNDB ficam como estão.</summary>
    internal LibraryTagFilter Renamed(string from, string to) =>
        new(Rewrite(Include, from, to), Any, Rewrite(Exclude, from, to));

    /// <summary>O filtro sem os termos que apontam para a tag própria <paramref name="name"/>.</summary>
    internal LibraryTagFilter Without(string name) =>
        new(Rewrite(Include, name, null), Any, Rewrite(Exclude, name, null));

    private static List<string> Terms(IEnumerable<string>? texts) =>
        [.. (texts ?? []).Select(t => LibraryTagTerm.Parse(t).ToString()).Distinct(StringComparer.Ordinal)];

    private static List<string> Rewrite(IReadOnlyList<string> terms, string from, string? to)
    {
        var result = new List<string>();
        foreach (var text in terms)
        {
            var term = LibraryTagTerm.Parse(text);
            if (term.Scope == LibraryTagScope.Vndb || term.Name != from)
            {
                result.Add(text);
            }
            else if (to is not null)
            {
                result.Add((term with { Name = to }).ToString());
            }
        }

        return [.. result.Distinct(StringComparer.Ordinal)];
    }
}
