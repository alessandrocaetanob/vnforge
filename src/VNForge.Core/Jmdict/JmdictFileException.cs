namespace VNForge.Core.Jmdict;

/// <summary>O dicionário não pôde ser baixado, montado ou lido. A mensagem diz o que aconteceu e o que a pessoa pode fazer; o dicionário que já estava instalado não é tocado.</summary>
public sealed class JmdictFileException : VNForgeException
{
    public JmdictFileException(string message)
        : base(message)
    {
    }

    public JmdictFileException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
