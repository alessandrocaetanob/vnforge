using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// O delimitador que fica entre o nome e a fala (<c>nome「fala」</c>) e o que o jogo desenha na caixa de texto. É o ponto único da escolha:
/// o injetor monta a mensagem com <see cref="Resolve"/> e <see cref="SpeakerMarkers"/> grava o mesmo caractere nos marcadores
/// <c>es.CHAR.NAME.MARK.SET</c> do script do sistema, para o jogo continuar achando o fim do nome.
/// </summary>
/// <remarks>
/// <para>
/// O jogo separa o nome da fala procurando os marcadores que o <c>yst00073</c> cadastra (<c>「</c>, <c>『</c> e <c>（</c>), e a abertura e o fechamento
/// <b>ficam na caixa de texto</b>. Em Shift-JIS são <c>81 75</c> e <c>81 76</c>; com a fonte em <c>cp1252</c> o <c>81</c> some e o segundo byte
/// vira letra (<c>u</c> e <c>v</c>, #434). A <c>cp1252</c> também não pode usar a aspa curva (<c>0x93</c> engole a letra seguinte, #390).
/// </para>
/// <para>
/// Quando a codificação do projeto não grava o japonês, vale <see cref="Cp1252Open"/> e <see cref="Cp1252Close"/>: caracteres de um byte fora da
/// faixa de lead bytes (<c>0xAB</c> e <c>0xBB</c>). Ficaram por não aparecerem em tradução nem em narração, o que evita o jogo tomar um trecho de
/// narração por nome; a alternativa são as aspas retas, que aparecem em qualquer fala. Para trocar, mude só as duas constantes.
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
    /// <summary>Abertura que a <c>cp1252</c> usa no lugar de <c>「</c> e <c>『</c>.</summary>
    public const string Cp1252Open = "«";

    /// <summary>Fechamento que a <c>cp1252</c> usa no lugar de <c>」</c> e <c>』</c>.</summary>
    public const string Cp1252Close = "»";

    /// <summary>Abertura que a <c>cp1252</c> usa no lugar de <c>（</c>, o marcador da fala de pensamento (<c>nome（fala）</c>, #448).</summary>
    public const string Cp1252ThoughtOpen = "(";

    /// <summary>Fechamento que a <c>cp1252</c> usa no lugar de <c>）</c>.</summary>
    public const string Cp1252ThoughtClose = ")";

    /// <summary>
    /// O que gravar no lugar de <paramref name="delimiter"/> (<c>「 」 『 』 （ ）</c>, ou vazio) com a codificação do projeto: o próprio delimitador se ela o grava,
    /// o par da <c>cp1252</c> se não e ela grava o par, e o delimitador de novo se nem o par é gravável (aí o chamador usa o Shift-JIS).
    /// </summary>
    public static string Resolve(ITextEncoder encoder, string delimiter)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(delimiter);
        if (delimiter.Length == 0 || encoder.FindUnencodable(delimiter).Count == 0)
        {
            return delimiter;
        }

        return Substitute(delimiter) is { } substitute && encoder.FindUnencodable(substitute).Count == 0 ? substitute : delimiter;
    }

    private static string? Substitute(string delimiter) => delimiter switch
    {
        "「" or "『" => Cp1252Open,
        "」" or "』" => Cp1252Close,
        "（" => Cp1252ThoughtOpen,
        "）" => Cp1252ThoughtClose,
        _ => null,
    };
}
