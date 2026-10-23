using System.Security.Cryptography;

namespace VNForge.Core.Injection;

internal static class FileHash
{
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
