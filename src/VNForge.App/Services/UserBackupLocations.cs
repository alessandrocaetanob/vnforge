using VNForge.Core.Injection;
using VNForge.Core.Library;

namespace VNForge.App.Services;

/// <summary>A pasta dos backups que a aba Backups dos Ajustes guarda no <c>library.json</c>, lida na hora de abrir um projeto.</summary>
public static class UserBackupLocations
{
    /// <summary>
    /// Um <c>library.json</c> ilegível não derruba a tela que só quer abrir um projeto: vale o padrão (<c>backup/</c> do projeto), e a Biblioteca já avisa do arquivo.
    /// </summary>
    public static BackupLocations Read(LibraryFile? file)
    {
        if (file is null)
        {
            return BackupLocations.Default;
        }

        try
        {
            return file.Load().BackupLocations;
        }
        catch (LibraryException)
        {
            return BackupLocations.Default;
        }
    }
}
