using System.Security.AccessControl;
using System.Security.Principal;

namespace VNForge.Core.Tests.Library;

/// <summary>Tira de si a permissão de listar uma pasta e a devolve no Dispose, para o teste conseguir limpar a pasta temporária.</summary>
internal sealed class DeniedAccess : IDisposable
{
    private readonly string _folder;
    private readonly UnixFileMode? _unixMode;
    private readonly FileSystemAccessRule? _rule;

    private DeniedAccess(string folder, UnixFileMode? unixMode, FileSystemAccessRule? rule)
    {
        _folder = folder;
        _unixMode = unixMode;
        _rule = rule;
    }

    /// <summary>Nega a leitura e confere que a negação vale; nulo quando o ambiente não a faz valer (processo com privilégio, por exemplo).</summary>
    public static DeniedAccess? Create(string folder)
    {
        DeniedAccess denied;
        if (OperatingSystem.IsWindows())
        {
            var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            var info = new DirectoryInfo(folder);
            var security = info.GetAccessControl();
            security.AddAccessRule(rule);
            info.SetAccessControl(security);
            denied = new DeniedAccess(folder, null, rule);
        }
        else
        {
            var mode = File.GetUnixFileMode(folder);
            File.SetUnixFileMode(folder, UnixFileMode.None);
            denied = new DeniedAccess(folder, mode, null);
        }

        try
        {
            _ = Directory.GetFileSystemEntries(folder);
        }
        catch (UnauthorizedAccessException)
        {
            return denied;
        }

        denied.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_rule is not null && OperatingSystem.IsWindows())
        {
            var info = new DirectoryInfo(_folder);
            var security = info.GetAccessControl();
            security.RemoveAccessRule(_rule);
            info.SetAccessControl(security);
        }
        else if (_unixMode is { } mode)
        {
            File.SetUnixFileMode(_folder, mode);
        }
    }
}
