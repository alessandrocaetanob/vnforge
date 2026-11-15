namespace VNForge.Core.Injection;

/// <summary>Um arquivo do jogo que não tem mais o hash que a extração leu.</summary>
/// <param name="Path">Caminho relativo à pasta do jogo, com <c>/</c>.</param>
/// <param name="ExpectedSha256">O SHA-256 que a extração gravou.</param>
/// <param name="FoundSha256">O SHA-256 do arquivo de agora; nulo quando ele sumiu.</param>
public sealed record ChangedGameFile(string Path, string ExpectedSha256, string? FoundSha256);

/// <summary>Por que um backup à mão não foi feito, quando a causa é a pasta dos backups e a saída é escolher outra nos Ajustes.</summary>
public enum BackupFolderProblem
{
    None,
    Unavailable,
    NotEnoughSpace,
}

/// <summary>Falha esperada de <c>inject</c> ou <c>restore</c> (build ausente, backup corrompido, hash divergente, gravação recusada).</summary>
public sealed class InjectionException : VNForgeException
{
    public InjectionException(string message)
        : base(message)
    {
    }

    public InjectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Quando a falha é da pasta dos backups (inacessível ou sem espaço), qual; a tela oferece os Ajustes.</summary>
    public BackupFolderProblem FolderProblem { get; init; }

    /// <summary>Os arquivos que mudaram desde a extração, quando a recusa é essa; vazio nas demais.</summary>
    public IReadOnlyList<ChangedGameFile> ChangedFiles { get; init; } = [];
}
