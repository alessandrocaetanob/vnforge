using VNForge.Core.Library;

namespace VNForge.Core.Factsheet;

/// <summary>O que um termo traduzido nomeia: uma tag do jogo, um traço de personagem ou o grupo do VNDB a que o traço pertence (Hair, Eyes...).</summary>
public enum SheetTermKind
{
    Tag,
    Trait,
    Group,
}

/// <summary>
/// Um nome do VNDB traduzido uma vez. <paramref name="Id"/> é o id do VNDB (<c>g123</c>, <c>i45</c>; o grupo de traços também é <c>i</c>, como <c>i10</c>); <paramref name="Context"/> é o que ajuda a IA a escolher o sentido
/// (a categoria da tag, o grupo do traço) e não faz parte da tradução.
/// </summary>
public sealed record SheetTerm(SheetTermKind Kind, string Id, string? Context, SheetText Text);

/// <summary>
/// As traduções de nomes de tag e de traço, uma por id do VNDB e valendo para todos os jogos e personagens: a tag "Comedy" se traduz uma vez, na primeira ficha que a
/// traz, e as outras só a leem. Em memória; <see cref="SheetStore"/> lê e grava.
/// </summary>
public sealed class SheetTerms
{
    private readonly Dictionary<string, SheetTerm> _terms = new(StringComparer.Ordinal);

    public IReadOnlyList<SheetTerm> All => [.. _terms.Values.OrderBy(t => t.Kind).ThenBy(t => t.Id, StringComparer.Ordinal)];

    /// <summary>A chave de um termo na ficha e na CLI: <c>tag:g12</c>, <c>trait:i34</c> ou <c>group:i10</c>.</summary>
    public static string KeyOf(SheetTermKind kind, string id) => $"{KindName(kind)}:{id}";

    public static string KindName(SheetTermKind kind) => kind switch
    {
        SheetTermKind.Tag => "tag",
        SheetTermKind.Group => "group",
        _ => "trait",
    };

    /// <summary>Os ids dos grupos de traços dos personagens, sem repetir e em ordem; traço de ficha gravada sem o id do grupo não conta.</summary>
    internal static IEnumerable<string> GroupIdsOf(IEnumerable<CharacterSheet> characters) =>
        characters.SelectMany(c => c.Traits).Select(t => t.GroupId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    public SheetTerm? Find(SheetTermKind kind, string id) => _terms.GetValueOrDefault(KeyOf(kind, id));

    /// <summary>O nome para mostrar: a tradução quando há, senão o nome do VNDB.</summary>
    public string Shown(SheetTermKind kind, string id, string vndbName) =>
        Find(kind, id) is { Text.HasTarget: true } term ? term.Text.Target! : vndbName;

    /// <summary>
    /// O nome do grupo de traços para mostrar, nesta ordem: a tradução da ficha (<c>group:i10</c>), o nome em português do dicionário fixo do app
    /// (<see cref="SheetLabels.TraitGroup"/>) e, por último, o nome do VNDB em inglês.
    /// </summary>
    public string ShownGroup(string groupId, string vndbName) =>
        !string.IsNullOrWhiteSpace(groupId) && Find(SheetTermKind.Group, groupId) is { Text.HasTarget: true } term ? term.Text.Target! : SheetLabels.TraitGroup(vndbName);

    /// <summary>
    /// Garante o termo com o nome que o VNDB tem agora. Termo novo entra pendente; um que já existe mantém a tradução, com as regras de <see cref="SheetText.WithSource"/>
    /// se o nome mudou. Devolve verdadeiro se o termo é novo.
    /// </summary>
    public bool Ensure(SheetTermKind kind, string id, string name, string? context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = KeyOf(kind, id);
        var source = name.Trim();
        if (_terms.TryGetValue(key, out var current))
        {
            _terms[key] = current with { Context = context, Text = current.Text.WithSource(source) };
            return false;
        }

        _terms[key] = new SheetTerm(kind, id, context, SheetText.Pending(source));
        return true;
    }

    /// <summary>Troca o texto de um termo que já existe.</summary>
    /// <exception cref="LibraryException">O termo não existe.</exception>
    public void Replace(SheetTermKind kind, string id, SheetText text)
    {
        var current = Find(kind, id) ?? throw new LibraryException($"O termo {KeyOf(kind, id)} não está na ficha.");
        _terms[KeyOf(kind, id)] = current with { Text = text };
    }

    /// <summary>Põe o termo como veio do arquivo.</summary>
    internal void Put(SheetTerm term) => _terms[KeyOf(term.Kind, term.Id)] = term;
}
