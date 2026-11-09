using System.Text.RegularExpressions;

namespace VNForge.Core.Memory;

/// <summary>Compara dois originais pelo texto que a pessoa lê, sem os marcadores do jogo (<c>\p</c>, <c>\n</c>) nem a diferença de espaços.</summary>
internal static partial class SourceSimilarity
{
    /// <summary>Semelhança mínima de uma correspondência aproximada. O corte de 80% é um palpite do quadro do #537, a calibrar com uso real.</summary>
    public const double DefaultMinimum = 0.80;

    [GeneratedRegex(@"\\[A-Za-z]|\{[^{}]*\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Markers();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Spaces();

    public static string Normalize(string source) => Spaces().Replace(Markers().Replace(source, " "), " ").Trim();

    /// <summary>
    /// Semelhança de 0 a 1 entre dois textos já normalizados (1 menos a distância de edição sobre o maior comprimento), ou 0 quando fica abaixo de
    /// <paramref name="minimum"/>. A conta para cedo: só aceita o número de edições que ainda deixa a semelhança acima do mínimo.
    /// </summary>
    public static double Compare(string a, string b, double minimum)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1;
        }

        var longest = Math.Max(a.Length, b.Length);
        var allowed = (int)Math.Floor((1 - minimum) * longest);
        if (Math.Abs(a.Length - b.Length) > allowed)
        {
            return 0;
        }

        var distance = BoundedDistance(a, b, allowed);
        return distance > allowed ? 0 : 1 - ((double)distance / longest);
    }

    /// <summary>Distância de Levenshtein limitada a uma faixa de <c>2 * limit + 1</c> colunas; devolve <c>limit + 1</c> quando passa do limite.</summary>
    private static int BoundedDistance(string a, string b, int limit)
    {
        var over = limit + 1;
        var previous = new int[b.Length + 2];
        var current = new int[b.Length + 2];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j <= limit ? j : over;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            var from = Math.Max(1, i - limit);
            var to = Math.Min(b.Length, i + limit);
            current[0] = i <= limit ? i : over;
            if (from > 1)
            {
                current[from - 1] = over;
            }

            var best = current[0];
            for (var j = from; j <= to; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                var deletion = previous[j] + 1;
                var insertion = current[j - 1] + 1;
                var value = Math.Min(over, Math.Min(substitution, Math.Min(deletion, insertion)));
                current[j] = value;
                best = Math.Min(best, value);
            }

            if (to < b.Length)
            {
                current[to + 1] = over;
            }

            if (best > limit)
            {
                return over;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[b.Length], over);
    }
}
