namespace VNForge.Core.Injection;

/// <summary>
/// As duas operações que alteram disco na injeção e na restauração. Existe para os testes simularem uma falha
/// no meio da gravação; em produção vale <see cref="PhysicalInjectionFileSystem"/>.
/// </summary>
public interface IInjectionFileSystem
{
    /// <summary>Copia <paramref name="source"/> para <paramref name="destination"/> de forma atômica: o destino só é trocado quando a cópia está completa.</summary>
    void CopyFile(string source, string destination);

    void DeleteFile(string path);
}
