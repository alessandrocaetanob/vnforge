using VNForge.Core.Library;
using VNForge.Core.Projects;
using VNForge.Patching;

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
            try
            {
                ProjectFileIo.Retry<int>(null, () =>
                {
                    ProjectDisk.Current.Copy(source, temporary);
                    return 0;
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw FileWriteVerificationException.ForFailedWrite(destination, e);
            }

            AtomicFile.Replace(temporary, destination);
        }
        finally
        {
            ProjectFileIo.DeleteQuietly(temporary);
        }
    }

    public void DeleteFile(string path) => File.Delete(path);
}
