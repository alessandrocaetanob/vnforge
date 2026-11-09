using System.Text;
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
        var value = (text ?? string.Empty).Normalize(NormalizationForm.FormKC).TrimStart();
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
    /// <summary>No lugar de um termo que a privacidade esconde, quando o filtro é mostrado (<see cref="Masked"/>).</summary>
    public const string HiddenTerm = "(tag escondida)";

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

    /// <summary>
    /// O filtro com a tag própria <paramref name="from"/> trocada por <paramref name="to"/>. Termos do VNDB ficam como estão. Se depois da troca <paramref name="to"/>
    /// está em <see cref="Include"/> e em <see cref="Exclude"/> (mesclar com uma tag que já era exclusão), o termo sai de <see cref="Include"/> e um aviso vai para <paramref name="notices"/>.
    /// </summary>
    internal LibraryTagFilter Renamed(string from, string to, List<string> notices)
    {
        var include = Rewrite(Include, from, term => term with { Name = to });
        var exclude = Rewrite(Exclude, from, term => term with { Name = to });
        var conflicts = include.Where(t => OwnNamed(t, to)).ToList();
        if (conflicts.Count > 0 && exclude.Any(t => OwnNamed(t, to)))
        {
            include = [.. include.Except(conflicts)];
            notices.Add($"No filtro salvo, '{to}' estava em --tag e em --not depois da mescla. Ficou só em --not.");
        }

        return new(include, Any, exclude);
    }

    /// <summary>
    /// O filtro sem os termos que apontam para a tag própria <paramref name="name"/>. O termo sem prefixo vira <c>vndb:nome</c> em vez de sair quando
    /// <paramref name="vndbStillMatches"/> (ainda há tag do VNDB com esse nome). Se a tag era tudo o que o filtro exigia, ele inteiro é apagado. Os avisos vão para <paramref name="notices"/>.
    /// </summary>
    internal LibraryTagFilter Without(string name, bool vndbStillMatches, List<string> notices)
    {
        var converted = false;
        LibraryTagTerm? Keep(LibraryTagTerm term)
        {
            if (term.Scope != LibraryTagScope.Any || !vndbStillMatches)
            {
                return null;
            }

            converted = true;
            return term with { Scope = LibraryTagScope.Vndb };
        }

        var include = Rewrite(Include, name, Keep);
        var exclude = Rewrite(Exclude, name, Keep);
        if ((Include.Count > 0 && include.Count == 0) || (!IsEmpty && include.Count + exclude.Count == 0))
        {
            notices.Add($"O filtro salvo foi apagado: ele dependia da tag '{name}'.");
            return Empty;
        }

        var lost = Include.Count + Exclude.Count - include.Count - exclude.Count;
        if (lost > 0)
        {
            notices.Add($"O filtro salvo perdeu {(lost == 1 ? "um termo" : $"{lost} termos")} que apontava para a tag '{name}'.");
        }

        if (converted)
        {
            notices.Add($"No filtro salvo, '{name}' agora vale só para a tag do VNDB com esse nome.");
        }

        return new(include, Any, exclude);
    }

    /// <summary>
    /// O filtro com os termos que a privacidade esconde trocados por <see cref="HiddenTerm"/>, para mostrar sem revelar. Um termo que não é só de tag própria
    /// esconde quando casa com alguma tag do VNDB escondida em <paramref name="games"/> e com nenhuma que a privacidade mostre; o nome de uma tag sua nunca esconde.
    /// </summary>
    public LibraryTagFilter Masked(IReadOnlyCollection<LibraryGame> games, UserTags tags, LibraryPrivacy privacy, out int hidden)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(privacy);
        var count = 0;
        string Mask(string text)
        {
            var term = LibraryTagTerm.Parse(text);
            if (term.Scope == LibraryTagScope.Own || (term.Scope == LibraryTagScope.Any && tags.TryGet(term.Name, out _)))
            {
                return text;
            }

            var tagsOfGames = games.SelectMany(g => g.Info?.Tags ?? []).Where(term.Matches).ToList();
            if (tagsOfGames.Any(t => LibraryVisibility.IsHidden(t, privacy)) && !tagsOfGames.Any(t => !LibraryVisibility.IsHidden(t, privacy)))
            {
                count++;
                return HiddenTerm;
            }

            return text;
        }

        var masked = new LibraryTagFilter([.. Include.Select(Mask)], Any, [.. Exclude.Select(Mask)]);
        hidden = count;
        return masked;
    }

    private static bool OwnNamed(string text, string name)
    {
        var term = LibraryTagTerm.Parse(text);
        return term.Scope != LibraryTagScope.Vndb && term.Name == name;
    }

    private static List<string> Terms(IEnumerable<string>? texts) =>
        [.. (texts ?? []).Select(t => LibraryTagTerm.Parse(t).ToString()).Distinct(StringComparer.Ordinal)];

    /// <summary>Aplica <paramref name="map"/> aos termos de tag própria chamados <paramref name="name"/> (nulo tira o termo); os demais ficam como estão.</summary>
    private static List<string> Rewrite(IReadOnlyList<string> terms, string name, Func<LibraryTagTerm, LibraryTagTerm?> map)
    {
        var result = new List<string>();
        foreach (var text in terms)
        {
            var term = LibraryTagTerm.Parse(text);
            if (term.Scope == LibraryTagScope.Vndb || term.Name != name)
            {
                result.Add(text);
            }
            else if (map(term) is { } mapped)
            {
                result.Add(mapped.ToString());
            }
        }

        return [.. result.Distinct(StringComparer.Ordinal)];
    }
}