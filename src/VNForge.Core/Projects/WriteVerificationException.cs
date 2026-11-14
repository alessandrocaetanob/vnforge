namespace VNForge.Core.Projects;

/// <summary>Uma gravação terminou sem erro do disco, mas ao reler o destino o conteúdo não é o que foi gravado (arquivo vazio ou diferente).</summary>
public sealed class WriteVerificationException : VNForgeException
{
    public WriteVerificationException(string message)
        : base(message)
    {
    }

    public WriteVerificationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
