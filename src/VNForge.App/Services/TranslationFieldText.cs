using System.Text;
using VNForge.Core.Formatting;

namespace VNForge.App.Services;

/// <summary>
/// O texto do campo da tradução sem marcadores crus. <c>\n</c> vira quebra de linha, <c>\p</c> no meio vira linha em branco e a virada de página do fim
/// (<c>\p</c> e <c>\c</c> depois do último texto) sai do campo e volta ao gravar; os demais marcadores continuam no texto, onde o editor os desenha como selos.
/// Texto que não volta igual por esse caminho (duas quebras seguidas, barra solta) fica cru no campo: gravar nunca muda um byte que a pessoa não tocou.
/// </summary>
public sealed class TranslationFieldText
{
    private const string PageTurn = @"\p";
    private const string LineBreak = @"\n";

    private TranslationFieldText(string text, string tail, bool raw)
    {
        Text = text;
        Tail = tail;
        IsRaw = raw;
    }

    /// <summary>O texto para pôr no campo.</summary>
    public string Text { get; }

    /// <summary>Os marcadores do fim da fala, guardados fora do campo; vazio quando o campo mostra o texto cru.</summary>
    public string Tail { get; }

    /// <summary>O campo mostra o texto gravado como está, com os marcadores crus.</summary>
    public bool IsRaw { get; }

    /// <summary>Abre o campo para a tradução gravada. Sem tradução, o fim da fala vem do original, como o jogo espera.</summary>
    public static TranslationFieldText Open(string stored, string source)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(source);

        if (stored.Length == 0)
        {
            return new TranslationFieldText(string.Empty, TailOf(source) ?? string.Empty, raw: false);
        }

        if (TailOf(stored) is { } tail && stored.Length > tail.Length)
        {
            var body = stored[..^tail.Length];
            var text = Encode(body);
            if (string.Equals(Decode(text), body, StringComparison.Ordinal))
            {
                return new TranslationFieldText(text, tail, raw: false);
            }
        }

        return new TranslationFieldText(stored, string.Empty, raw: true);
    }

    /// <summary>O que vai para <c>text/&lt;script&gt;.json</c> para o texto que está no campo. Campo vazio grava vazio.</summary>
    public string ToStored(string fieldText)
    {
        ArgumentNullException.ThrowIfNull(fieldText);

        if (fieldText.Length == 0)
        {
            return fieldText;
        }

        // Cru, a quebra digitada (Enter) também vira o marcador: uma quebra de linha de verdade nunca vai para o arquivo do jogo.
        return IsRaw ? NormalizeBreaks(fieldText).Replace("\n", LineBreak, StringComparison.Ordinal) : Decode(fieldText) + Tail;
    }

    /// <summary>Os marcadores de virada de página no fim do texto; nulo quando o texto tem barra ou chave fora da gramática.</summary>
    private static string? TailOf(string text)
    {
        IReadOnlyList<TextSegment> segments;
        try
        {
            segments = ControlMarkers.Split(text);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var length = 0;
        for (var i = segments.Count - 1; i >= 0 && segments[i] is { IsMarker: true, Text: PageTurn or @"\c" }; i--)
        {
            length += segments[i].Text.Length;
        }

        return text[^length..];
    }

    private static string Encode(string body)
    {
        return string.Concat(ControlMarkers.Split(body).Select(segment => segment.Text switch
        {
            LineBreak => "\n",
            PageTurn => "\n\n",
            _ => segment.Text,
        }));
    }

    private static string NormalizeBreaks(string field) => field.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string Decode(string field)
    {
        var text = NormalizeBreaks(field);
        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '\n')
            {
                result.Append(text[i++]);
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] == '\n')
            {
                i++;
            }

            var run = i - start;
            result.Insert(result.Length, PageTurn, run / 2);
            result.Insert(result.Length, LineBreak, run % 2);
        }

        return result.ToString();
    }
}
