using System.Text.RegularExpressions;

namespace VNForge.Core.Vndb;

/// <summary>
/// Limpa os textos do VNDB (sinopse, descrição de personagem), que vêm com a marcação própria do site: <c>[url=...]texto[/url]</c>, <c>[b]</c>, <c>[spoiler]</c>.
/// Os trechos de spoiler saem inteiros, porque a sinopse aparece na tela de quem ainda não jogou.
/// </summary>
public static partial class VndbText
{
    /// <summary>O texto sem a marcação do VNDB: links ficam só com o texto, spoilers somem e as linhas em branco repetidas viram uma. Nulo ou vazio devolvem nulo.</summary>
    public static string? Clean(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = description.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Spoiler().Replace(text, string.Empty);
        text = LabeledUrl().Replace(text, "$1");
        text = BareUrl().Replace(text, "$1");
        text = Formatting().Replace(text, string.Empty);
        text = BlankLines().Replace(text, "\n\n");
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"\[spoiler\].*?\[/spoiler\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Spoiler();

    [GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LabeledUrl();

    [GeneratedRegex(@"\[url\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\[/?(?:b|i|u|s|quote|code|raw)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Formatting();

    [GeneratedRegex(@"[ \t]*\n(?:[ \t]*\n){2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BlankLines();
}
