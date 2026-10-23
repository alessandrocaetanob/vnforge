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
            File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // O temporário fica para trás, mas a falha original não pode ser mascarada.
            }
        }
    }

    public void DeleteFile(string path) => File.Delete(path);
}
