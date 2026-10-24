using System.Text.RegularExpressions;

namespace VNForge.App.Controls;

public enum DiffKind
{
    Equal,
    Insert,
    Delete,
}

public readonly record struct DiffSegment(string Text, DiffKind Kind);

/// <summary>
/// Diferença por palavra entre a tradução atual e a sugestão da IA (docs/design/components/AiSuggestion): maior subsequência comum,
/// sobre o texto sem marcadores de pausa (<c>\p</c>, <c>\c</c>, <c>\u</c>) e com <c>\n</c> como espaço, como o canvas faz.
/// Sem tradução atual, devolve a sugestão inteira sem marcação.
/// </summary>
public static partial class WordDiff
{
    public static IReadOnlyList<DiffSegment> Compute(string? current, string? suggestion)
    {
        var x = Words(current);
        var y = Words(suggestion);
        if (x.Length == 0)
        {
            return y.Select(w => new DiffSegment(w, DiffKind.Equal)).ToList();
        }

        var lcs = new int[x.Length + 1, y.Length + 1];
        for (var i = x.Length - 1; i >= 0; i--)
        {
            for (var j = y.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = x[i] == y[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffSegment>(x.Length + y.Length);
        int a = 0, b = 0;
        while (a < x.Length && b < y.Length)
        {
            if (x[a] == y[b])
            {
                result.Add(new DiffSegment(x[a++], DiffKind.Equal));
                b++;
            }
            else if (lcs[a + 1, b] >= lcs[a, b + 1])
            {
                result.Add(new DiffSegment(x[a++], DiffKind.Delete));
            }
            else
            {
                result.Add(new DiffSegment(y[b++], DiffKind.Insert));
            }
        }

        while (a < x.Length)
        {
            result.Add(new DiffSegment(x[a++], DiffKind.Delete));
        }

        while (b < y.Length)
        {
            result.Add(new DiffSegment(y[b++], DiffKind.Insert));
        }

        return result;
    }

    private static string[] Words(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : Spaces().Split(Pauses().Replace(text, "").Replace("\\n", " ")).Where(w => w.Length > 0).ToArray();

    [GeneratedRegex(@"\\[pcu]")]
    private static partial Regex Pauses();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
