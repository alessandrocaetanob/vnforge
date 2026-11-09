using System.Security.Cryptography;

namespace VNForge.Patching;

internal static class GamePaths
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>O caminho de um arquivo do patch dentro da pasta do jogo; um caminho que sai da pasta é recusado, venha de onde vier.</summary>
    public static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal))
        {
            throw new PatchException($"O patch tem um caminho inválido: {relative}.");
        }

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison))
        {
            throw new PatchException($"O patch tem um caminho que sai da pasta do jogo: {relative}.");
        }

        return full;
    }

    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string? ShaOrNull(string path) => File.Exists(path) ? Sha256(path) : null;
}
