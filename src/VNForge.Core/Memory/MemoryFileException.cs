namespace VNForge.Core.Memory;

/// <summary>Um arquivo do usuário (memória de tradução ou glossário pessoal) não pôde ser lido ou gravado. Nada é sobrescrito: a pessoa decide o que fazer com o arquivo.</summary>
public sealed class MemoryFileException(string filePath, string reason, Exception? innerException = null)
    : VNForgeException($"O arquivo '{filePath}' não pôde ser usado: {reason}", innerException)
{
    public string FilePath { get; } = filePath;
}
