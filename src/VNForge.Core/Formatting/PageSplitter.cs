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
/// O texto devolvido é a tradução como está gravada, com o <c>\p</c> a mais: o pipeline do perfil roda só para medir, e a posição do corte volta ao
/// texto original (o build aplica as etapas do perfil na hora). O <c>\p</c> ocupa o lugar do espaço da emenda ou da quebra de linha do autor que
/// termina a última linha que cabe. Sem como achar o ponto no texto original, a divisão é recusada e o texto volta igual.
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
        if (profile.Wrap.MaxLines is not { } maxLines)
        {
            return new PageSplit(translation, Measure(profile, formatted, Built(profile, formatted, translation)).Lines.Count, 0);
        }

        var current = translation;
        var added = 0;
        var original = Measure(profile, formatted, Built(profile, formatted, translation)).Lines.Count;
        while (true)
        {
            var built = Built(profile, formatted, current);
            var (laidOut, lines) = Measure(profile, formatted, built);
            var over = lines.FindIndex(count => count > maxLines);
            var next = over < 0 ? null : InsertPage(profile, formatted, current, built, laidOut, (over, maxLines));
            if (next is null)
            {
                // Sem ponto confiável e ainda transbordando: uma divisão pela metade não resolve, então o texto volta como veio.
                return over >= 0 && added > 0 ? new PageSplit(translation, original, 0) : new PageSplit(current, lines.Count, added);
            }

            current = next;
            added++;
        }
    }

    /// <summary>O que o build mediria: a tradução depois das etapas do perfil que antecedem a quebra, quando o papel da linha é formatado.</summary>
    private static string Built(FormattingProfile profile, bool formatted, string text) => formatted ? Prepare(profile, text) : text;

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
    /// Coloca <c>\p</c> em <paramref name="raw"/>, a tradução como está gravada, onde <paramref name="laidOut"/> (o texto medido com as quebras
    /// automáticas) termina a última linha que cabe na caixa. O ponto é achado em <paramref name="built"/> e levado de volta a <paramref name="raw"/>;
    /// sem um ponto confiável nos dois textos, devolve nulo e a divisão é recusada.
    /// </summary>
    private static string? InsertPage(FormattingProfile profile, bool formatted, string raw, string built, string laidOut, (int Box, int MaxLines) target)
    {
        var end = FindLineEnd(laidOut, target.Box, target.MaxLines);
        var inBuilt = end < 0 ? -1 : MapPosition(built, laidOut, end);
        var at = inBuilt < 0 || !formatted ? inBuilt : RawPosition(profile, raw, built[..inBuilt]);
        if (at < 0)
        {
            return null;
        }

        var afterSpaces = SkipSpaces(raw, at);
        var userBreak = IsAt(raw, afterSpaces, LineBreak);
        var resume = afterSpaces + (userBreak ? LineBreak.Length : 0);
        return string.Concat(raw.AsSpan(0, at), PageBreak, raw.AsSpan(resume));
    }

    /// <summary>
    /// O índice em <paramref name="raw"/> onde o texto preparado chega a <paramref name="builtPrefix"/>: o primeiro ponto cujo começo, depois
    /// das mesmas etapas do perfil, é igual a ele (sem os espaços do fim). Nulo quando nenhum limite bate, por exemplo numa regra que muda o texto
    /// de um jeito que não se desfaz.
    /// </summary>
    private static int RawPosition(FormattingProfile profile, string raw, string builtPrefix)
    {
        var wanted = builtPrefix.TrimEnd(' ');
        for (var i = 0; i <= raw.Length; i++)
        {
            try
            {
                if (string.Equals(Prepare(profile, raw[..i]).TrimEnd(' '), wanted, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormattingException)
            {
                // O corte caiu dentro de um marcador: não é um ponto válido.
            }
        }

        return -1;
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
