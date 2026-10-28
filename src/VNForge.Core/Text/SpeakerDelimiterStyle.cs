namespace VNForge.Core.Text;

/// <summary>
/// Como a fala com falante aparece na caixa de texto quando a codificação não grava as aspas japonesas <c>「 」</c> (hoje só a <c>cp1252</c>, #453).
/// O jogo precisa de um marcador de abertura na mensagem para separar o nome da fala; o estilo decide o que a pessoa vê no lugar dele.
/// </summary>
public enum SpeakerDelimiterStyle
{
    /// <summary>Sem sinal visível: a abertura é um caractere que a fonte desenha em branco e a fala não leva fechamento.</summary>
    None,

    /// <summary>Entre <c>« »</c>, o estilo do #434: o par aparece na caixa de texto.</summary>
    Guillemets,
}
