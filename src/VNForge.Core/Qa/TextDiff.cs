using System.Text;

namespace VNForge.Core.Qa;

/// <summary>O que um trecho de <see cref="TextDiff.Compare"/> é na comparação de uma variante com a referência.</summary>
public enum TextDiffKind
{
    /// <summary>Está nos dois textos.</summary>
    Same,

    /// <summary>Só a variante tem.</summary>
    Added,

    /// <summary>Só a referência tem: falta na variante, na posição do trecho.</summary>
    Missing,
}

/// <summary>Um trecho da comparação. <see cref="Visible"/> é o mesmo texto com os espaços trocados por símbolos que aparecem na tela.</summary>
public readonly record struct TextDiffSegment(string Text, TextDiffKind Kind)
{
    /// <summary>O trecho com espaço, tabulação e quebra de linha desenhados (<see cref="TextDiff.Visible"/>).</summary>
    public string Visible => TextDiff.Visible(Text);
}

/// <summary>
/// Diferença por caractere entre a tradução de referência e a de uma variante (E7, "mesmo original, traduções diferentes"): a maior subsequência comum
/// depois de tirar o começo e o fim iguais. Os trechos em ordem formam a variante (<see cref="TextDiffKind.Same"/> e <see cref="TextDiffKind.Added"/>) e a
/// referência (<see cref="TextDiffKind.Same"/> e <see cref="TextDiffKind.Missing"/>), então a tela mostra o texto da variante com o que difere marcado.
/// </summary>
public static class TextDiff
{
    // Acima disto o miolo diferente não cabe numa tabela de comprimentos razoável: vira um bloco só de faltando e acrescentado.
    private const int MaxCells = 1_500_000;

    /// <summary>Compara <paramref name="variant"/> com <paramref name="reference"/>. Textos iguais viram um trecho <see cref="TextDiffKind.Same"/> só.</summary>
    public static IReadOnlyList<TextDiffSegment> Compare(string reference, string variant)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(variant);
        if (string.Equals(reference, variant, StringComparison.Ordinal))
        {
            return reference.Length == 0 ? [] : [new TextDiffSegment(reference, TextDiffKind.Same)];
        }

        var a = Runes(reference);
        var b = Runes(variant);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            start++;
        }

        var end = 0;
        while (end < a.Length - start && end < b.Length - start && a[^(end + 1)] == b[^(end + 1)])
        {
            end++;
        }

        var steps = new List<(TextDiffKind Kind, int Rune)>(a.Length + b.Length);
        steps.AddRange(a.Take(start).Select(r => (TextDiffKind.Same, r)));
        MiddleSteps(a.AsSpan(start, a.Length - start - end), b.AsSpan(start, b.Length - start - end), steps);
        steps.AddRange(a.Skip(a.Length - end).Select(r => (TextDiffKind.Same, r)));
        return Merge(steps);
    }

    /// <summary>
    /// O texto como a tela o mostra num trecho que difere: espaço e espaço fino viram <c>·</c>, o sem quebra <c>°</c>, o ideográfico <c>□</c>,
    /// a tabulação <c>→</c> e a quebra de linha <c>↵</c>. Sem isso, um espaço duplo ou no fim não aparece.
    /// </summary>
    public static string Visible(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '\t' => '→',
                '\n' or '\r' => '↵',
                ' ' or ' ' => '°',
                '　' => '□',
                _ when char.IsWhiteSpace(c) => '·',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static int[] Runes(string text) => [.. text.EnumerateRunes().Select(r => r.Value)];

    private static void MiddleSteps(ReadOnlySpan<int> a, ReadOnlySpan<int> b, List<(TextDiffKind Kind, int Rune)> steps)
    {
        if (a.Length == 0 || b.Length == 0 || (long)(a.Length + 1) * (b.Length + 1) > MaxCells)
        {
            steps.AddRange(a.ToArray().Select(r => (TextDiffKind.Missing, r)));
            steps.AddRange(b.ToArray().Select(r => (TextDiffKind.Added, r)));
            return;
        }

        var width = b.Length + 1;
        var lcs = new int[(a.Length + 1) * width];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[(i * width) + j] = a[i] == b[j]
                    ? lcs[((i + 1) * width) + j + 1] + 1
                    : Math.Max(lcs[((i + 1) * width) + j], lcs[(i * width) + j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                steps.Add((TextDiffKind.Same, a[x]));
                x++;
                y++;
            }
            else if (lcs[((x + 1) * width) + y] >= lcs[(x * width) + y + 1])
            {
                steps.Add((TextDiffKind.Missing, a[x++]));
            }
            else
            {
                steps.Add((TextDiffKind.Added, b[y++]));
            }
        }

        while (x < a.Length)
        {
            steps.Add((TextDiffKind.Missing, a[x++]));
        }

        while (y < b.Length)
        {
            steps.Add((TextDiffKind.Added, b[y++]));
        }
    }

    /// <summary>
    /// Junta os passos em trechos. Um caractere igual espremido entre duas mudanças (a letra que sobra por acaso numa troca de palavra) não vira trecho:
    /// entra nas duas mudanças, para a marcação não ficar picada. Dentro de cada bloco de mudança o que falta vem antes do que foi acrescentado.
    /// </summary>
    private static List<TextDiffSegment> Merge(List<(TextDiffKind Kind, int Rune)> steps)
    {
        var runs = new List<(TextDiffKind Kind, StringBuilder Text, int Runes)>();
        foreach (var (kind, rune) in steps)
        {
            if (runs.Count == 0 || runs[^1].Kind != kind)
            {
                runs.Add((kind, new StringBuilder(), 0));
            }

            var last = runs[^1];
            last.Text.Append(char.ConvertFromUtf32(rune));
            runs[^1] = last with { Runes = last.Runes + 1 };
        }

        var result = new List<TextDiffSegment>();
        var missing = new StringBuilder();
        var added = new StringBuilder();
        for (var i = 0; i < runs.Count; i++)
        {
            var (kind, text, count) = runs[i];
            var squeezed = kind == TextDiffKind.Same && count == 1 && i > 0 && i < runs.Count - 1;
            if (kind != TextDiffKind.Same || squeezed)
            {
                if (kind != TextDiffKind.Added)
                {
                    missing.Append(text);
                }

                if (kind != TextDiffKind.Missing)
                {
                    added.Append(text);
                }

                continue;
            }

            Flush(result, missing, added);
            result.Add(new TextDiffSegment(text.ToString(), TextDiffKind.Same));
        }

        Flush(result, missing, added);
        return result;
    }

    private static void Flush(List<TextDiffSegment> result, StringBuilder missing, StringBuilder added)
    {
        if (missing.Length > 0)
        {
            result.Add(new TextDiffSegment(missing.ToString(), TextDiffKind.Missing));
            missing.Clear();
        }

        if (added.Length > 0)
        {
            result.Add(new TextDiffSegment(added.ToString(), TextDiffKind.Added));
            added.Clear();
        }
    }
}
