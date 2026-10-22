namespace VNForge.Core;

/// <summary>O arquivo existe, mas o conteúdo não segue o formato esperado.</summary>
public sealed class InvalidFileException : VNForgeException
{
    public InvalidFileException(string path, string reason, Exception? innerException = null)
        : base($"O arquivo '{path}' é inválido: {reason}", innerException)
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }

    public string Reason { get; }
}
