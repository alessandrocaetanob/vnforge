using System.Globalization;
using System.Text.RegularExpressions;

namespace VNForge.Core.Search;

/// <summary>A busca compilada: texto literal ou regex, sempre com tempo limite por linha.</summary>
internal sealed class SearchPattern
{
    private readonly Regex regex;
    private readonly bool useRegex;
    private readonly bool wholeWord;

    private SearchPattern(Regex regex, bool useRegex, bool wholeWord)
    {
        this.regex = regex;
        this.useRegex = useRegex;
        this.wholeWord = wholeWord;
    }

    public static SearchPattern Compile(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Pattern))
        {
            throw new SearchException("Digite o texto a procurar.");
        }

        var body = options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new SearchPattern(new Regex(body, flags, options.MatchTimeout), options.UseRegex, options.WholeWord);
        }
        catch (ArgumentException e)
        {
            throw new SearchException("A expressão regular é inválida. Confira parênteses, colchetes e barras, ou desligue a opção de regex.", e);
        }
    }

    /// <summary>
    /// As ocorrências não vazias em <paramref name="text"/>, em ordem. Em palavra inteira, o vizinho de cada ocorrência é conferido no texto,
    /// com os <paramref name="markers"/> (marcadores de controle) tratados como separador; assim <c>\n</c> não vira a letra "n".
    /// </summary>
    /// <param name="emptyMatches">Quantas ocorrências vazias foram descartadas.</param>
    public IReadOnlyList<Match> Matches(string text, IReadOnlyList<TextRange> markers, out int emptyMatches)
    {
        var matches = new List<Match>();
        emptyMatches = 0;
        try
        {
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length == 0)
                {
                    emptyMatches++;
                }
                else if (!wholeWord || IsWholeWord(text, match, markers))
                {
                    matches.Add(match);
                }
            }
        }
        catch (RegexMatchTimeoutException e)
        {
            throw new SearchException("A busca demorou demais numa linha e foi interrompida. Simplifique a expressão regular (evite grupos repetidos dentro de grupos repetidos).", e);
        }

        return matches;
    }

    /// <summary>O texto que entra no lugar de <paramref name="match"/>: <paramref name="replacement"/> literal, ou com <c>$1</c> e <c>${nome}</c> expandidos na regex.</summary>
    public string Expand(Match match, string replacement) =>
        useRegex && replacement.Contains('$', StringComparison.Ordinal) ? match.Result(replacement) : replacement;

    private static bool IsWholeWord(string text, Match match, IReadOnlyList<TextRange> markers)
    {
        var before = match.Index - 1;
        if (before >= 0 && char.IsLowSurrogate(text[before]) && before > 0 && char.IsHighSurrogate(text[before - 1]))
        {
            before--;
        }

        var after = match.Index + match.Length;
        return !(before >= 0 && IsWordChar(text, before, markers)) && !(after < text.Length && IsWordChar(text, after, markers));
    }

    private static bool IsWordChar(string text, int index, IReadOnlyList<TextRange> markers)
    {
        foreach (var marker in markers)
        {
            if (index >= marker.Start && index < marker.Start + marker.Length)
            {
                return false;
            }
        }

        return CharUnicodeInfo.GetUnicodeCategory(text, index) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.ConnectorPunctuation => true,
            _ => false,
        };
    }
}
