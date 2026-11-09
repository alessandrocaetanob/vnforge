using System.Text.RegularExpressions;
using VNForge.Core.Library;

namespace VNForge.Core.Factsheet;

/// <summary>Os textos de uma ficha que se traduzem, revisam e aprovam.</summary>
public enum SheetFieldKind
{
    Synopsis,
    Character,
    Tag,
    Trait,
}

/// <summary>
/// Um texto da ficha pelo nome que a CLI e a tela usam: <c>synopsis</c>, <c>character:c12</c> (ou só <c>c12</c>), <c>tag:g7</c> ou <c>trait:i34</c>. É também a
/// <see cref="SheetTranslationItem.Key"/> do pedido à IA.
/// </summary>
public sealed partial record SheetField(SheetFieldKind Kind, string? Id)
{
    public static SheetField Synopsis { get; } = new(SheetFieldKind.Synopsis, null);

    public static SheetField OfCharacter(string id) => new(SheetFieldKind.Character, id);

    public static SheetField OfTag(string id) => new(SheetFieldKind.Tag, id);

    public static SheetField OfTrait(string id) => new(SheetFieldKind.Trait, id);

    /// <summary>A chave textual: <c>synopsis</c>, <c>character:c12</c>, <c>tag:g7</c>, <c>trait:i34</c>.</summary>
    public string Key => Kind switch
    {
        SheetFieldKind.Synopsis => "synopsis",
        SheetFieldKind.Character => $"character:{Id}",
        SheetFieldKind.Tag => $"tag:{Id}",
        _ => $"trait:{Id}",
    };

    public override string ToString() => Key;

    /// <exception cref="LibraryException">O texto não é uma das formas acima.</exception>
    public static SheetField Parse(string text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (string.Equals(value, "synopsis", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "sinopse", StringComparison.OrdinalIgnoreCase))
        {
            return Synopsis;
        }

        var match = Pattern().Match(value);
        if (!match.Success)
        {
            throw new LibraryException($"'{value}' não é um texto da ficha. Use synopsis, character:c12 (ou c12), tag:g7 ou trait:i34.");
        }

        var id = match.Groups["id"].Value.ToLowerInvariant();
        var kind = match.Groups["kind"].Value.ToLowerInvariant() switch
        {
            "character" or "personagem" => SheetFieldKind.Character,
            "tag" => SheetFieldKind.Tag,
            "trait" or "traco" or "traço" => SheetFieldKind.Trait,
            _ => id[0] switch
            {
                'c' => SheetFieldKind.Character,
                'g' => SheetFieldKind.Tag,
                _ => SheetFieldKind.Trait,
            },
        };
        var expected = kind switch
        {
            SheetFieldKind.Character => 'c',
            SheetFieldKind.Tag => 'g',
            _ => 'i',
        };
        return id[0] == expected
            ? new SheetField(kind, id)
            : throw new LibraryException($"'{value}': o id de {Describe(kind)} começa com {expected}.");
    }

    private static string Describe(SheetFieldKind kind) => kind switch
    {
        SheetFieldKind.Character => "personagem",
        SheetFieldKind.Tag => "tag",
        _ => "traço",
    };

    [GeneratedRegex(@"^(?:(?<kind>character|personagem|tag|trait|traco|traço):)?(?<id>[cgi]\d{1,9})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Pattern();
}
