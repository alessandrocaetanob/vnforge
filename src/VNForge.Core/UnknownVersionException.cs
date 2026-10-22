namespace VNForge.Core;

/// <summary>O arquivo declara uma versão de formato que o VNForge não conhece.</summary>
public sealed class UnknownVersionException : VNForgeException
{
    public UnknownVersionException(string path, string version, Exception? innerException = null)
        : base($"Versão desconhecida ({version}) no arquivo '{path}'.", innerException)
    {
        Path = path;
        Version = version;
    }

    public string Path { get; }

    public string Version { get; }
}
