using System.Security.Cryptography;
using System.Text;

namespace VNForge.Core.Projects;

/// <summary>A impressão digital do texto original de uma linha: a chave do índice por original e do cache da explicação da IA.</summary>
public static class SourceHash
{
    /// <summary>SHA-256 do original em UTF-8, em hexadecimal minúsculo (64 caracteres).</summary>
    public static string Of(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
}
