namespace VNForge.Core.Text;

/// <summary>Um arquivo <c>text/*.json</c> não pôde ser lido: não existe, não é UTF-8, não segue o formato ou tem ids que não são do script.</summary>
public sealed class TextFileException : VNForgeException
{
    public TextFileException(string filePath, string reason, Exception? innerException = null)
        : base($"Arquivo de texto '{filePath}' inválido: {reason}", innerException)
    {
        FilePath = filePath;
        Reason = reason;
    }

    /// <summary>Caminho do arquivo (ou o nome que o chamador deu a ele).</summary>
    public string FilePath { get; }

    /// <summary>Motivo, em português, com o campo e a posição quando o erro é do conteúdo.</summary>
    public string Reason { get; }
}
