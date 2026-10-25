using VNForge.Core.Text;

namespace VNForge.Core.Formatting;

/// <summary>O resultado de <see cref="PageSplitter.Split"/>.</summary>
/// <param name="Text">A tradução com as viradas de página inseridas; a recebida, sem mudança, quando já cabia ou não há como dividir.</param>
/// <param name="Pages">Quantas caixas (páginas) o texto tem depois da divisão.</param>
/// <param name="Added">Quantas viradas de página <c>\p</c> a divisão acrescentou.</param>
public sealed record PageSplit(string Text, int Pages, int Added)
{
    /// <summary>Verdadeiro quando a divisão acrescentou ao menos uma virada de página.</summary>
    public bool Changed => Added > 0;
}

/// <summary>
/// Divide uma fala que não cabe na caixa do jogo em páginas: insere <c>\p</c> no fim da última linha que cabe em cada caixa com mais linhas que
/// o <c>wrap.maxLines</c> do perfil. Mede com o mesmo pipeline e a mesma quebra de linha do build e do <see cref="Projects.LinePreviewer"/>.
/// </summary>
/// <remarks>
/// O texto devolvido é a tradução depois das etapas do perfil que antecedem a quebra (espaços, tipografia), sem as quebras automáticas: elas
/// ficam por conta do build, que as refaz com a largura da caixa. As quebras <c>\n</c> do autor são mantidas, e a que termina a última linha
/// que cabe vira a virada de página.
/// </remarks>
public static class PageSplitter
{
    private const string LineBreak = @"\n";
    private const string PageBreak = @"\p";

    /// <summary>Divide <paramref name="translation"/> em páginas que cabem na caixa de <paramref name="profile"/>.</summary>
    /// <param name="profile">Perfil de formatação do projeto.</param>
    /// <param name="unit">A linha; só o papel (<see cref="TextUnit.Kind"/>) é usado, para saber se o build a formata.</param>
    /// <param name="translation">A tradução com marcadores, gravada ou em edição.</param>
    /// <exception cref="VNForgeException">Texto que o pipeline recusa (marcador inválido).</exception>
    public static PageSplit Split(FormattingProfile profile, TextUnit unit, string translation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(translation);

        try
        {
            return SplitCore(profile, profile.Kinds.Contains(unit.Kind), translation);
        }
        catch (Exception ex) when (ex is ArgumentException or FormattingException)
        {
            throw new VNForgeException($"Não foi possível dividir a linha {unit.Id} em páginas: {ex.Message}", ex);
        }
    }

    private static PageSplit SplitCore(FormattingProfile profile, bool formatted, string translation)
    {
        var current = formatted ? Prepare(profile, translation) : translation;
        if (profile.Wrap.MaxLines is not { } maxLines)
        {
            return new PageSplit(translation, Measure(profile, formatted, current).Lines.Count, 0);
        }

        var added = 0;
        while (true)
        {
            var (laidOut, lines) = Measure(profile, formatted, current);
            var over = lines.FindIndex(count => count > maxLines);
            var next = over < 0 ? null : InsertPage(current, laidOut, over, maxLines);
            if (next is null)
            {
                return added == 0 || BuildFits(profile, formatted, current, maxLines)
                    ? new PageSplit(added == 0 ? translation : current, lines.Count, added)
                    : new PageSplit(translation, Measure(profile, formatted, formatted ? Prepare(profile, translation) : translation).Lines.Count, 0);
            }

            current = next;
            added++;
        }
    }

    /// <summary>
    /// Confere o texto dividido como o build o trataria: as etapas anteriores à quebra rodam de novo sobre ele. Uma regra de substituição que não
    /// é idempotente (<c>a</c> vira <c>aa</c>) faria a fala passar do limite de novo, e nesse caso a divisão é descartada.
    /// </summary>
    private static bool BuildFits(FormattingProfile profile, bool formatted, string text, int maxLines) =>
        Measure(profile, formatted, formatted ? Prepare(profile, text) : text).Lines.All(count => count <= maxLines);

    /// <summary>As etapas do perfil até antes da quebra de linha, que é a última do pipeline.</summary>
    private static string Prepare(FormattingProfile profile, string text) =>
        FormattingPipeline.Create(profile with { Wrap = profile.Wrap with { Enabled = false } }).Format(text).Text;

    private static (string LaidOut, List<int> Lines) Measure(FormattingProfile profile, bool formatted, string text)
    {
        var options = profile.Wrap.ToOptions();
        var laidOut = formatted && profile.Wrap.Enabled ? WordWrapper.Wrap(text, options).Text : text;
        var lines = LineMeasure.Columns(laidOut, options.Width).Select(box => box.Count).ToList();
        return (laidOut, lines);
    }

    /// <summary>
    /// Coloca <c>\p</c> em <paramref name="text"/> onde <paramref name="laidOut"/> (o mesmo texto com as quebras automáticas) termina a linha
    /// <paramref name="maxLines"/> da caixa <paramref name="box"/>. Nulo quando não acha o ponto.
    /// </summary>
    private static string? InsertPage(string text, string laidOut, int box, int maxLines)
    {
        var end = FindLineEnd(laidOut, box, maxLines);
        if (end < 0)
        {
            return null;
        }

        var at = MapPosition(text, laidOut, end);
        if (at < 0)
        {
            return null;
        }

        var afterSpaces = SkipSpaces(text, at);
        var userBreak = string.CompareOrdinal(text, afterSpaces, LineBreak, 0, LineBreak.Length) == 0;
        var resume = afterSpaces + (userBreak ? LineBreak.Length : 0);
        return string.Concat(text.AsSpan(0, at), PageBreak, text.AsSpan(resume));
    }

    /// <summary>Posição, em <paramref name="laidOut"/>, do <c>\n</c> que termina a linha <paramref name="line"/> da caixa <paramref name="box"/>.</summary>
    private static int FindLineEnd(string laidOut, int box, int line)
    {
        int boxIndex = 0, ended = 0, i = 0;
        while (i < laidOut.Length)
        {
            var length = TokenLength(laidOut, i);
            if (IsAt(laidOut, i, LineBreak))
            {
                ended++;
                if (boxIndex == box && ended == line)
                {
                    return i;
                }
            }
            else if (IsAt(laidOut, i, PageBreak))
            {
                boxIndex++;
                ended = 0;
            }

            i += length;
        }

        return -1;
    }

    /// <summary>
    /// O índice em <paramref name="text"/> que corresponde à posição <paramref name="target"/> de <paramref name="laidOut"/>. A quebra automática
    /// só acrescenta <c>\n</c> e tira os espaços da emenda, então os dois textos andam juntos, pulando uma coisa ou outra.
    /// </summary>
    private static int MapPosition(string text, string laidOut, int target)
    {
        int t = 0, w = 0;
        while (w < target)
        {
            if (IsAt(laidOut, w, LineBreak))
            {
                var afterSpaces = SkipSpaces(text, t);
                if (!IsAt(text, afterSpaces, LineBreak))
                {
                    w += LineBreak.Length;
                    continue;
                }

                t = afterSpaces;
            }

            var length = TokenLength(laidOut, w);
            if (t < text.Length && string.CompareOrdinal(text, t, laidOut, w, length) == 0)
            {
                t += length;
                w += length;
            }
            else if (t < text.Length && text[t] == ' ')
            {
                t++;
            }
            else
            {
                return -1;
            }
        }

        return t;
    }

    private static int SkipSpaces(string text, int from)
    {
        var i = from;
        while (i < text.Length && text[i] == ' ')
        {
            i++;
        }

        return i;
    }

    private static bool IsAt(string text, int index, string value) =>
        index >= 0 && index + value.Length <= text.Length && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    /// <summary>Tamanho em caracteres do próximo trecho: um escape de dois caracteres, um marcador entre chaves ou um caractere.</summary>
    private static int TokenLength(string text, int index)
    {
        switch (text[index])
        {
            case '\\':
                return Math.Min(2, text.Length - index);
            case '{':
                var close = text.IndexOf('}', index);
                return close < 0 ? 1 : close - index + 1;
            default:
                return char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
        }
    }
}
