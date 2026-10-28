namespace VNForge.Core.Text;

/// <summary>
/// O que o jogo desenha em volta da fala de uma linha com falante, além do texto da tradução: a abertura (na <c>cp1252</c> sem sinal, o <c>0xA0</c> que a fonte desenha
/// em branco, uma coluna) e o fechamento (vazio sem sinal, <c>»</c> com <c>« »</c>). A prévia da caixa de texto os mede como aparecem no jogo (#453); o pipeline de
/// formatação não os conhece e quebra as linhas só pela tradução.
/// </summary>
/// <param name="Open">Texto antes da fala; vazio quando a codificação grava as aspas do original (Shift-JIS), que a prévia não mede.</param>
/// <param name="Close">Texto depois da fala.</param>
public readonly record struct SpeakerBoxMarks(string Open, string Close)
{
    /// <summary>Sem abertura nem fechamento: a prévia mede só a tradução.</summary>
    public static SpeakerBoxMarks None => new(string.Empty, string.Empty);

    /// <summary>Os delimitadores de <paramref name="encoder"/> quando ele os tem (<see cref="ISpeakerDelimiterEncoder"/>); <see cref="None"/> nos outros e sem encoder.</summary>
    public static SpeakerBoxMarks For(ITextEncoder? encoder) =>
        encoder is ISpeakerDelimiterEncoder own ? new SpeakerBoxMarks(own.SpeakerOpen, own.SpeakerClose) : None;

    /// <summary>Os delimitadores da estratégia <paramref name="strategy"/> com o estilo <paramref name="style"/>; <see cref="None"/> quando a estratégia não é conhecida ou ainda não tem encoder.</summary>
    public static SpeakerBoxMarks For(string strategy, SpeakerDelimiterStyle style)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        try
        {
            return For(TextEncoderRegistry.Default.Create(strategy, style));
        }
        catch (VNForgeException)
        {
            return None;
        }
    }

    /// <summary><paramref name="text"/> com a abertura e o fechamento quando a linha tem falante; o próprio texto na narração.</summary>
    public string Wrap(TextUnit unit, string text)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(text);
        return string.IsNullOrEmpty(unit.EffectiveSpeaker) ? text : Open + text + Close;
    }
}
