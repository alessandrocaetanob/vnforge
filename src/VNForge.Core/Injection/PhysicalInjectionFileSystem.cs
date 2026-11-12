using VNForge.Core.Projects;

namespace VNForge.Core.Injection;

/// <summary>Disco real. A cópia vai para um temporário ao lado do destino e só então o substitui.</summary>
public sealed class PhysicalInjectionFileSystem : IInjectionFileSystem
{
    public void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Antivírus ou indexador podem segurar o arquivo por instantes (origem, temporário ou destino): a espera é a do ProjectFileIo. O que é copiado e a ordem não mudam.
            ProjectFileIo.Retry<int>(null, () =>
            {
                File.Copy(source, temporary, overwrite: true);
                return 0;
            });
            ProjectFileIo.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            ProjectFileIo.DeleteQuietly(temporary);
        }
    }

    public void DeleteFile(string path) => File.Delete(path);
}
