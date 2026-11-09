namespace VNForge.Patching;

/// <summary>O patch não pôde ser lido, conferido ou aplicado. A mensagem já está pronta para o jogador.</summary>
public sealed class PatchException : Exception
{
    public PatchException(string message)
        : base(message)
    {
    }

    public PatchException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>As etapas do patch, na ordem em que o instalador as mostra.</summary>
public enum PatchPhase
{
    /// <summary>Calcular as diferenças (ao gerar o patch).</summary>
    Diff,

    /// <summary>Conferir a versão do jogo.</summary>
    Check,

    /// <summary>Guardar a cópia dos originais.</summary>
    Backup,

    /// <summary>Aplicar o patch.</summary>
    Apply,

    /// <summary>Conferir o resultado.</summary>
    Verify,

    /// <summary>Devolver os originais.</summary>
    Restore,
}

/// <param name="Path">O arquivo em andamento; nulo no começo e no fim de uma etapa.</param>
/// <param name="Done">Arquivos já feitos nesta etapa.</param>
/// <param name="Total">Arquivos desta etapa.</param>
public sealed record PatchProgress(PatchPhase Phase, string? Path, int Done, int Total);
