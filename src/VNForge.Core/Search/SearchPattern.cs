using System.Text.RegularExpressions;

namespace VNForge.Core.Search;

/// <summary>A busca compilada: texto literal ou regex, sempre com tempo limite por linha.</summary>
internal sealed class SearchPattern
{
    private const string WordEdgeBefore = @"(?<![\p{L}\p{N}_])";
    private const string WordEdgeAfter = @"(?![\p{L}\p{N}_])";

    private readonly Regex regex;
    private readonly bool useRegex;

    private SearchPattern(Regex regex, bool useRegex)
    {
        this.regex = regex;
        this.useRegex = useRegex;
    }

    public static SearchPattern Compile(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Pattern))
        {
            throw new SearchException("Digite o texto a procurar.");
        }

        var body = options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        if (options.WholeWord)
        {
            body = $"{WordEdgeBefore}(?:{body}){WordEdgeAfter}";
        }

        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new SearchPattern(new Regex(body, flags, options.MatchTimeout), options.UseRegex);
        }
        catch (ArgumentException e)
        {
            throw new SearchException("A expressão regular é inválida. Confira parênteses, colchetes e barras, ou desligue a opção de regex.", e);
        }
    }

    /// <summary>As ocorrências não vazias em <paramref name="text"/>, em ordem.</summary>
    public IReadOnlyList<Match> Matches(string text)
    {
        var matches = new List<Match>();
        try
        {
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length > 0)
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
}
