using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>Um trecho da tradução: texto comum ou marcador de controle.</summary>
public readonly record struct TextSegment(string Text, bool IsMarker);

/// <summary>
/// Leitor dos marcadores de controle da tradução (doc 02): <c>\n</c>, <c>\p</c>, <c>\c</c>, <c>\u</c>, <c>{...}</c> e os escapes
/// <c>\\</c> e <c>\{</c>. Cada um é um trecho opaco que as regras de formatação não podem tocar.
/// </summary>
public static class ControlMarkers
{
    /// <summary>Quebra de linha dentro da caixa.</summary>
    public const string LineBreak = @"\n";

    /// <summary>Virada de página: o jogo espera o clique e abre uma caixa nova.</summary>
    public const string PageBreak = @"\p";

    /// <summary>Divide <paramref name="text"/> em texto comum e marcadores; juntar os trechos devolve o texto original.</summary>
    /// <exception cref="ArgumentException">Barra no fim, marcador desconhecido ou chave sem fechamento.</exception>
    public static IReadOnlyList<TextSegment> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var segments = new List<TextSegment>();
        var plain = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c != '\\' && c != '{')
            {
                plain.Append(c);
                i++;
                continue;
            }

            var marker = ReadMarker(text, i);

            if (plain.Length > 0)
            {
                segments.Add(new TextSegment(plain.ToString(), IsMarker: false));
                plain.Clear();
            }

            segments.Add(new TextSegment(marker, IsMarker: true));
            i += marker.Length;
        }

        if (plain.Length > 0)
        {
            segments.Add(new TextSegment(plain.ToString(), IsMarker: false));
        }

        return segments;
    }

    private static string ReadMarker(string text, int i)
    {
        if (text[i] == '{')
        {
            var close = text.IndexOf('}', i);
            if (close < 0)
            {
                throw new ArgumentException("Chave '{' sem fechamento no texto.", nameof(text));
            }

            return text[i..(close + 1)];
        }

        if (i + 1 >= text.Length)
        {
            throw new ArgumentException("Barra invertida no fim do texto, sem caractere de marcador.", nameof(text));
        }

        var marker = text.Substring(i, 2);
        if (marker[1] is not ('n' or 'p' or 'c' or 'u' or '\\' or '{'))
        {
            throw new ArgumentException($"Marcador inválido '{marker}' no texto.", nameof(text));
        }

        return marker;
    }

    /// <summary>Os marcadores de <paramref name="text"/>, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> Markers(string text) =>
        [.. Split(text).Where(s => s.IsMarker).Select(s => s.Text)];
}
