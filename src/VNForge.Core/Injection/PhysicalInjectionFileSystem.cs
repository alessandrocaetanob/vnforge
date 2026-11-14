using VNForge.Core.Library;

namespace VNForge.Core.Injection;

/// <summary>Disco real.</summary>
public sealed class PhysicalInjectionFileSystem : IInjectionFileSystem
{
    /// <summary>Copia por <see cref="AtomicFile.Copy"/>: a cópia é conferida contra a origem, então um arquivo do jogo copiado pela metade não passa.</summary>
    public void CopyFile(string source, string destination) => AtomicFile.Copy(source, destination);

    public void DeleteFile(string path) => File.Delete(path);
}
