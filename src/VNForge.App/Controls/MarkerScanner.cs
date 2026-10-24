namespace VNForge.App.Controls;

/// <summary>Tipo de marcador: de controle (<c>\n</c>, <c>\p</c>, <c>\c</c>, <c>\u</c> e os escapes) ou variável (<c>{NOME:valor}</c>).</summary>
public enum MarkerKind
{
    Control,
    Variable,
}

/// <summary>Posição e tipo de um marcador dentro de um texto.</summary>
public readonly record struct MarkerSpan(int Start, int Length, MarkerKind Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// Acha os marcadores de uma linha de tradução, com a mesma gramática do <c>ControlMarkers</c> do Core, mas tolerante: o usuário digita um
/// marcador letra a letra, então barra solta, marcador desconhecido e chave sem fechamento ficam como texto comum em vez de dar erro.
/// </summary>
public static class MarkerScanner
{
    public static IReadOnlyList<MarkerSpan> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var spans = new List<MarkerSpan>();
        var i = 0;
        while (i < text.Length)
        {
            if (TryReadAt(text, i) is { } span)
            {
                spans.Add(span);
                i = span.End;
            }
            else
            {
                i++;
            }
        }

        return spans;
    }

    /// <summary>O primeiro marcador de <paramref name="text"/> que começa em <paramref name="startIndex"/> ou depois, lendo o texto desde o início para não confundir <c>\\</c> com um marcador.</summary>
    public static MarkerSpan? FindFrom(string text, int startIndex)
    {
        foreach (var span in Scan(text))
        {
            if (span.Start >= startIndex)
            {
                return span;
            }
        }

        return null;
    }

    private static MarkerSpan? TryReadAt(string text, int i)
    {
        if (text[i] == '{')
        {
            var close = text.IndexOf('}', i + 1);
            return close > i + 1 ? new MarkerSpan(i, close - i + 1, MarkerKind.Variable) : null;
        }

        if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is 'n' or 'p' or 'c' or 'u' or '\\' or '{')
        {
            return new MarkerSpan(i, 2, MarkerKind.Control);
        }

        return null;
    }
}
