using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VNForge.Patching;

/// <summary>
/// O disco real. Quem decide se um caminho é local é o volume onde ele está de fato (o ponto de montagem, não a letra da raiz): um rclone montado numa pasta
/// (<c>C:\mnt\x</c>) tem <see cref="Path.GetPathRoot(string)"/> NTFS e seria tomado por local. <paramref name="volumeOf"/> devolve o volume do caminho (nulo na dúvida)
/// e <paramref name="isLocalVolume"/> diz se ele é local; o teste troca os dois.
/// </summary>
internal sealed partial class PhysicalProjectDisk(Func<string, string?> volumeOf, Func<string, bool> isLocalVolume) : IProjectDisk
{
    // O veredito de um volume vale um minuto: uma pasta remontada ou uma letra trocada de disco não fica com o veredito antigo pelo processo todo.
    private static readonly TimeSpan VerdictLifetime = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (bool Local, long At)> _volumes = new(StringComparer.OrdinalIgnoreCase);

    public PhysicalProjectDisk()
        : this(OperatingSystem.IsWindows() ? WindowsVolumeOf : _ => "/", OperatingSystem.IsWindows() ? WindowsIsLocal : _ => true)
    {
    }

    public bool IsLocal(string path)
    {
        var volume = volumeOf(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(volume))
        {
            return false;
        }

        if (_volumes.TryGetValue(volume, out var known) && Stopwatch.GetElapsedTime(known.At) < VerdictLifetime)
        {
            return known.Local;
        }

        var local = isLocalVolume(volume);
        _volumes[volume] = (local, Stopwatch.GetTimestamp());
        return local;
    }

    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    // Formatos de disco local: o rename sobre arquivo existente é confiável neles. exFAT e FAT32 são os de pendrive e de HD externo (Fixed ou Removable).
    private static readonly HashSet<string> LocalFormats = new(["NTFS", "ReFS", "exFAT", "FAT32", "FAT"], StringComparer.OrdinalIgnoreCase);

    private const int MaxPath = 32768;

    private const uint DriveRemovable = 2;

    private const uint DriveFixed = 3;

    private const uint DriveRamDisk = 6;

    // O caminho já com os links (junção, link simbólico) resolvidos: uma pasta C:\projeto que aponta para W:\x está em W:.
    private static string? WindowsVolumeOf(string fullPath)
    {
        var real = ResolveLinks(fullPath);
        var volume = new char[MaxPath];
        return GetVolumePathNameW(real, volume, (uint)volume.Length) ? new string(volume, 0, Array.IndexOf(volume, '\0')) : null;
    }

    private static string ResolveLinks(string fullPath)
    {
        try
        {
            var remainder = new Stack<string>();
            for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
            {
                if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint) && current.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                {
                    return remainder.Aggregate(target.FullName, Path.Combine);
                }

                remainder.Push(current.Name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Sem como resolver, o caminho como está; o volume dele ainda é conferido.
        }

        return fullPath;
    }

    private static bool WindowsIsLocal(string volume)
    {
        if (volume.StartsWith(@"\\", StringComparison.Ordinal) && !volume.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var type = GetDriveTypeW(volume);
        if (type is not (DriveFixed or DriveRemovable or DriveRamDisk))
        {
            return false;
        }

        var format = new char[64];
        if (!GetVolumeInformationW(volume, null, 0, out _, out _, out _, format, (uint)format.Length))
        {
            return false;
        }

        var name = new string(format, 0, Array.IndexOf(format, '\0'));
        return LocalFormats.Contains(name);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathNameW(string fileName, [Out, MarshalUsing(CountElementName = nameof(bufferLength))] char[] volumePathName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveTypeW(string rootPathName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationW(
        string rootPathName,
        [Out, MarshalUsing(CountElementName = nameof(volumeNameSize))] char[]? volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        [Out, MarshalUsing(CountElementName = nameof(fileSystemNameSize))] char[] fileSystemNameBuffer,
        uint fileSystemNameSize);
}
