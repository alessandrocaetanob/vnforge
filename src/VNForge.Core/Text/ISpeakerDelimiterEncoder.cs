namespace VNForge.Core.Text;

/// <summary>
/// Codificador que não grava as aspas japonesas de uma fala com falante (<c>「 」</c>) e por isso tem os próprios delimitadores: a abertura que o jogo
/// procura para separar o nome da fala e o fechamento, que pode ser vazio. A engine troca por eles o delimitador do original e o marcador do script do sistema.
/// </summary>
public interface ISpeakerDelimiterEncoder : ITextEncoder
{
    /// <summary>A escolha de <see cref="SpeakerDelimiterStyle"/> com que o codificador foi criado.</summary>
    SpeakerDelimiterStyle SpeakerDelimiter { get; }

    /// <summary>Abertura da fala no lugar de <c>「</c> e <c>『</c>. Um caractere de um byte fora da faixa de lead bytes; nunca vazia.</summary>
    string SpeakerOpen { get; }

    /// <summary>Fechamento da fala no lugar de <c>」</c> e <c>』</c>; vazio quando a fala não leva fechamento.</summary>
    string SpeakerClose { get; }

    /// <summary>
    /// Os bytes de <see cref="SpeakerOpen"/> ou de <see cref="SpeakerClose"/>. <see cref="ITextEncoder.Encode"/> pode recusar o caractere da abertura
    /// na tradução (ele é o marcador do jogo), então os delimitadores passam por aqui.
    /// </summary>
    /// <exception cref="ArgumentException">O texto não é a abertura nem o fechamento.</exception>
    byte[] EncodeSpeakerDelimiter(string delimiter);
}
