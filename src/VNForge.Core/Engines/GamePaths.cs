namespace VNForge.Core.Engines;

/// <summary>Resolução segura de caminhos relativos à pasta de um jogo.</summary>
public static class GamePaths
{
    /// <summary>
    /// Resolve <paramref name="relative"/> dentro de <paramref name="gameDir"/> e devolve o caminho completo.
    /// Recusa caminho vazio, enraizado ou com unidade (<c>C:foo</c>, <c>\foo</c>, <c>/foo</c>, UNC), qualquer
    /// <c>:</c> (inclui fluxos alternativos NTFS) e o que sai da pasta (<c>..</c>). A barra invertida conta como
    /// separador em qualquer plataforma. O caminho resultante tem de ficar dentro da pasta, com fronteira de
    /// separador: <c>gameDirX</c> não é a pasta <c>gameDir</c>.
    /// </summary>
    /// <param name="gameDir">Pasta do jogo.</param>
    /// <param name="relative">Caminho relativo à pasta do jogo.</param>
    /// <exception cref="ArgumentException">O caminho é inválido ou sai da pasta.</exception>
    public static string Resolve(string gameDir, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);

        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new ArgumentException("O caminho relativo está vazio.", nameof(relative));
        }

        if (relative.Contains(':'))
        {
            throw new ArgumentException($"O caminho '{relative}' tem ':' (unidade ou fluxo alternativo).", nameof(relative));
        }

        var normalized = relative.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(relative) || Path.IsPathRooted(normalized))
        {
            throw new ArgumentException($"O caminho '{relative}' não é relativo à pasta do jogo.", nameof(relative));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir));
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new ArgumentException($"O caminho '{relative}' sai da pasta do jogo.", nameof(relative));
        }

        return full;
    }
}
