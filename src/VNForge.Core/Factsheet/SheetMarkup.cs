using System.Text.RegularExpressions;

namespace VNForge.Core.Factsheet;

/// <summary>
/// O texto do VNDB como a ficha o guarda: sem a formatação do site (<c>[b]</c>, <c>[url=...]</c>), mas com os trechos <c>[spoiler]...[/spoiler]</c> no lugar. Eles ficam
/// porque a ficha esconde o spoiler e o mostra quando a pessoa pede, e a IA os mantém na tradução. Quem alimenta o Glossário e a bíblia, que não podem levar spoiler,
/// passa por <see cref="StripSpoilers"/>.
/// </summary>
public static partial class SheetMarkup
{
    private const int RegexTimeoutMilliseconds = 500;

    /// <summary>O texto com os links reduzidos ao rótulo, a formatação tirada, os marcadores de spoiler em minúsculas e as linhas em branco repetidas viradas uma. Nulo ou vazio devolvem nulo.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = text.ReplaceLineEndings("\n");
        result = LabeledUrl().Replace(result, "$1");
        result = BareUrl().Replace(result, "$1");
        result = Formatting().Replace(result, string.Empty);
        result = SpoilerOpen().Replace(result, "[spoiler]");
        result = SpoilerClose().Replace(result, "[/spoiler]");
        result = BlankLines().Replace(result, "\n\n").Trim();
        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// O texto sem os trechos de spoiler; um <c>[spoiler]</c> sem <c>[/spoiler]</c> vale até o fim, como na Biblioteca (<see cref="Library.LibraryVisibility"/>).
    /// Nulo quando não sobra nada.
    /// </summary>
    public static string? StripSpoilers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = SpoilerBlock().Replace(text, string.Empty);
        result = RepeatedSpaces().Replace(result, " ");
        result = TrailingSpaces().Replace(result, "\n");
        result = BlankLines().Replace(result, "\n\n").Trim();
        return result.Length == 0 ? null : result;
    }

    /// <summary>Quantos <c>[spoiler]</c> abrem e quantos <c>[/spoiler]</c> fecham. A tradução tem que devolver os mesmos números.</summary>
    public static (int Open, int Close) SpoilerMarkers(string? text) =>
        string.IsNullOrEmpty(text) ? (0, 0) : (SpoilerOpen().Count(text), SpoilerClose().Count(text));

    public static bool HasSpoilers(string? text) => SpoilerMarkers(text).Open > 0;

    [GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex LabeledUrl();

    [GeneratedRegex(@"\[url\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\[/?(?:b|i|u|s|quote|code|raw)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Formatting();

    [GeneratedRegex(@"\[spoiler\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex SpoilerOpen();

    [GeneratedRegex(@"\[/spoiler\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex SpoilerClose();

    [GeneratedRegex(@"\[spoiler\].*?(?:\[/spoiler\]|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex SpoilerBlock();

    [GeneratedRegex(@"(?<=\S)[ \t]{2,}(?=\S)", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex RepeatedSpaces();

    [GeneratedRegex(@"[ \t]+\n", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n(?:[ \t]*\n){2,}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex BlankLines();
}
