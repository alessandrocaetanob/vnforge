namespace VNForge.Core.Glossary;

/// <summary>Um <c>glossary.json</c>, <c>characters.json</c> ou <c>scripts.json</c> não pôde ser lido ou gravado: não é UTF-8, não segue o formato ou tem entradas repetidas.</summary>
public sealed class GlossaryFileException : VNForgeException
{
    public GlossaryFileException(string filePath, string reason, Exception? innerException = null)
        : base($"Arquivo '{filePath}' inválido: {reason}", innerException)
    {
        FilePath = filePath;
        Reason = reason;
    }

    /// <summary>Caminho do arquivo (ou o nome que o chamador deu a ele).</summary>
    public string FilePath { get; }

    /// <summary>Motivo, em português, com o campo e a posição quando o erro é do conteúdo.</summary>
    public string Reason { get; }
}
