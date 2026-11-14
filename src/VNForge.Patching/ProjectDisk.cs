namespace VNForge.Patching;

/// <summary>
/// O que a gravação conferida (<c>AtomicFile</c>) precisa do disco e que o teste troca: se o volume do arquivo é um disco local (NTFS, ReFS) onde o rename sobre um
/// arquivo existente é confiável, e a própria troca. Num disco de rede ou FUSE a troca pode falhar sem avisar (rclone com cache em modo full devolve sucesso e
/// deixa o destino com 0 byte), então o Core usa outro caminho e confere o resultado.
/// </summary>
public interface IProjectDisk
{
    /// <summary>O volume de <paramref name="path"/> é um disco local onde trocar por rename é seguro. Na dúvida, falso.</summary>
    bool IsLocal(string path);

    void Move(string source, string destination, bool overwrite);
}

/// <summary>O disco que o Core usa; <see cref="Use"/> troca por um falso só na cadeia de chamadas do teste (não vaza para testes em paralelo).</summary>
public static class ProjectDisk
{
    private static readonly AsyncLocal<IProjectDisk?> Override = new();

    private static readonly PhysicalProjectDisk Physical = new();

    public static IProjectDisk Current => Override.Value ?? Physical;

    /// <summary>Usa <paramref name="disk"/> até o descarte do retorno.</summary>
    public static IDisposable Use(IProjectDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var previous = Override.Value;
        Override.Value = disk;
        return new Restore(previous);
    }

    /// <summary>Aviso em português para o projeto num volume que não é local; nulo quando é local.</summary>
    public static string? NonLocalWarning(string path) =>
        Current.IsLocal(path)
            ? null
            : $"Aviso: '{path}' está num disco que não é local (rede, FUSE ou formato diferente de NTFS/ReFS). O VNForge confere cada gravação relendo o arquivo e usa um caminho mais lento; se a gravação falhar, o arquivo anterior fica guardado ao lado, com a extensão .bak.";

    private sealed class Restore(IProjectDisk? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}

internal sealed class PhysicalProjectDisk : IProjectDisk
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _roots = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLocal(string path)
    {
        // Só o Windows tem o problema conhecido (rclone sobre WinFsp); nos outros sistemas DriveInfo não acha o ponto de montagem de um caminho qualquer.
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return !string.IsNullOrEmpty(root) && _roots.GetOrAdd(root, Probe);
    }

    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    private static bool Probe(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            var localFormat = drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
                || drive.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
            return localFormat && drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
