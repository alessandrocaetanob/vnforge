using VNForge.Core.Formatting;

namespace VNForge.Core.Qa;

/// <summary>As regras de texto do E7.4 e o leitor de trechos que elas compartilham.</summary>
public static class TextQaRules
{
    /// <summary>As regras de texto, na ordem em que rodam.</summary>
    public static IReadOnlyList<IQaRule> All { get; } =
        [new DoubleSpaceRule(), new SpaceBeforePunctuationRule(), new UnbalancedPairsRule(), new LeftoverJapaneseRule()];

    /// <summary>
    /// Os trechos de texto comum de <paramref name="text"/> com o índice de cada um na linha inteira; os marcadores de controle ficam de fora.
    /// Vazio quando o texto tem marcador inválido: quem confere marcadores é outra regra, estas só olham texto.
    /// </summary>
    internal static IReadOnlyList<(int Offset, string Text)> PlainRuns(string text)
    {
        IReadOnlyList<TextSegment> segments;
        try
        {
            segments = ControlMarkers.Split(text);
        }
        catch (ArgumentException)
        {
            return [];
        }

        var runs = new List<(int, string)>();
        var offset = 0;
        foreach (var segment in segments)
        {
            if (!segment.IsMarker)
            {
                runs.Add((offset, segment.Text));
            }

            offset += segment.Text.Length;
        }

        return runs;
    }

    internal static QaSpan Span(string text, int start, int length) => new(start, length, text.Substring(start, length));
}
