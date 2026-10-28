using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// O delimitador que fica entre o nome e a fala (<c>nome「fala」</c>) e o que o jogo desenha na caixa de texto. É o ponto único da escolha:
/// o injetor monta a mensagem com <see cref="Resolve"/> e <see cref="SpeakerMarkers"/> grava o mesmo caractere nos marcadores
/// <c>es.CHAR.NAME.MARK.SET</c> do script do sistema, para o jogo continuar achando o fim do nome.
/// </summary>
/// <remarks>
/// <para>
/// O jogo separa o nome da fala procurando os marcadores que o <c>yst00073</c> cadastra (<c>「</c>, <c>『</c> e <c>（</c>), e a abertura
/// <b>fica na caixa de texto</b>: no original japonês a caixa mostra <c>「fala」</c>. Só marcadores de abertura são cadastrados, então o fechamento não participa
/// da separação. Em Shift-JIS são <c>81 75</c> e <c>81 76</c>; com a fonte em <c>cp1252</c> o <c>81</c> some e o segundo byte
/// vira letra (<c>u</c> e <c>v</c>, #434). A <c>cp1252</c> também não pode usar a aspa curva (<c>0x93</c> engole a letra seguinte, #390).
/// </para>
/// <para>
/// Quando a codificação do projeto não grava o japonês, valem os delimitadores dela (<see cref="ISpeakerDelimiterEncoder"/>, escolhidos por
/// <see cref="SpeakerDelimiterStyle"/>): caracteres de um byte fora da faixa de lead bytes. Sem sinal (#453) a abertura é o <c>0xA0</c>, que a fonte desenha em branco,
/// e a fala não leva fechamento; com <c>« »</c> (#434) o par aparece na caixa. Nos dois casos o caractere não aparece em narração, o que evita o jogo tomar um
/// trecho de narração por nome.
/// </para>
/// <para>
/// A fala de pensamento (<c>nome（fala）</c>) é outra forma: o <c>（ ）</c> faz parte do corpo da tradução, que já sai com os parênteses ASCII
/// (<see cref="Cp1252ThoughtOpen"/> e <see cref="Cp1252ThoughtClose"/>; o corretor e a pontuação do teclado trocam <c>（ ）</c> por eles), e o
/// marcador do <c>yst00073</c> precisa ser esse mesmo caractere. Valem <c>(</c> e <c>)</c> por serem o que a tradução já tem e o que o leitor espera ver
/// em torno de um pensamento, e porque não repetem o <c>«</c> da fala. O custo é que <c>(</c> também aparece em narração; o risco está no PR do #448.
/// </para>
/// </remarks>
public static class SpeakerDelimiters
{
    /// <summary>Abertura que a <c>cp1252</c> usa no lugar de <c>（</c>, o marcador da fala de pensamento (<c>nome（fala）</c>, #448).</summary>
    public const string Cp1252ThoughtOpen = "(";

    /// <summary>Fechamento que a <c>cp1252</c> usa no lugar de <c>）</c>.</summary>
    public const string Cp1252ThoughtClose = ")";

    /// <summary>
    /// O que gravar no lugar de <paramref name="delimiter"/> (<c>「 」 『 』 （ ）</c>, ou vazio) com a codificação do projeto: o próprio delimitador se ela o grava,
    /// o delimitador dela se não e ela tem (<see cref="ISpeakerDelimiterEncoder"/>; o fechamento sai vazio sem sinal), e o delimitador de novo se não há substituto
    /// (aí o chamador usa o Shift-JIS).
    /// </summary>
    public static string Resolve(ITextEncoder encoder, string delimiter)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(delimiter);
        if (delimiter.Length == 0 || encoder.FindUnencodable(delimiter).Count == 0)
        {
            return delimiter;
        }

        return Substitute(encoder, delimiter) ?? delimiter;
    }

    /// <summary>Verdadeiro se <paramref name="resolved"/> é a abertura ou o fechamento do próprio <paramref name="encoder"/>, que <see cref="ToBytes"/> grava mesmo quando <see cref="ITextEncoder.Encode"/> recusa o caractere.</summary>
    public static bool IsOwn(ITextEncoder encoder, string resolved)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(resolved);
        return resolved.Length > 0
            && encoder is ISpeakerDelimiterEncoder own
            && (string.Equals(resolved, own.SpeakerOpen, StringComparison.Ordinal) || string.Equals(resolved, own.SpeakerClose, StringComparison.Ordinal));
    }

    /// <summary>Os bytes de um delimitador já resolvido (<see cref="Resolve"/>): os do próprio codificador quando é dele, senão os do texto comum.</summary>
    public static byte[] ToBytes(ITextEncoder encoder, string resolved) =>
        IsOwn(encoder, resolved) ? ((ISpeakerDelimiterEncoder)encoder).EncodeSpeakerDelimiter(resolved) : ControlMarkers.ToBytes(resolved, encoder);

    private static string? Substitute(ITextEncoder encoder, string delimiter)
    {
        var own = encoder as ISpeakerDelimiterEncoder;
        var substitute = delimiter switch
        {
            "「" or "『" => own?.SpeakerOpen,
            "」" or "』" => own?.SpeakerClose,
            "（" => Cp1252ThoughtOpen,
            "）" => Cp1252ThoughtClose,
            _ => null,
        };

        return substitute is not null && (substitute.Length == 0 || IsOwn(encoder, substitute) || encoder.FindUnencodable(substitute).Count == 0) ? substitute : null;
    }
}
