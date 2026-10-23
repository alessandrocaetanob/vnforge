using VNForge.Core;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>Resultado de <see cref="YpfFolder.Unpack"/>.</summary>
/// <param name="FileCount">Arquivos gravados.</param>
/// <param name="ByteCount">Soma dos tamanhos descomprimidos.</param>
public sealed record YpfUnpackResult(int FileCount, long ByteCount);

/// <summary>Resultado de <see cref="YpfFolder.Pack"/>.</summary>
/// <param name="EntryCount">Entradas do pacote gravado (as mesmas do original).</param>
/// <param name="Replaced">Caminhos cujo arquivo na pasta difere do conteúdo do original.</param>
/// <param name="Missing">Caminhos sem arquivo na pasta; a entrada do original foi mantida.</param>
public sealed record YpfPackResult(int EntryCount, IReadOnlyList<string> Replaced, IReadOnlyList<string> Missing);

/// <summary>
/// Extração de um YPF para uma pasta e remontagem a partir dela. A remontagem usa
/// <see cref="YpfArchive.WriteTo"/>: só entra como troca o arquivo que difere do conteúdo do original, e o resto
/// é copiado byte a byte (regra 3 do <c>CLAUDE.md</c>).
/// </summary>
public static class YpfFolder
{
    /// <summary>
    /// Grava cada entrada, já descomprimida, em <paramref name="directory"/>, com o caminho interno como caminho relativo.
    /// Confere todos os caminhos antes de gravar o primeiro byte; arquivos que já existem são sobrescritos.
    /// </summary>
    /// <exception cref="VNForgeException">Um caminho interno sai da pasta, é inválido, ou colide com outro neste sistema de arquivos.</exception>
    public static YpfUnpackResult Unpack(YpfArchive archive, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = Path.GetFullPath(directory);
        var targets = ResolveTargets(archive, root);

        long bytes = 0;
        for (var i = 0; i < archive.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[i];
            Directory.CreateDirectory(Path.GetDirectoryName(targets[i])!);
            using var content = archive.OpenEntry(entry);
            using var file = new FileStream(targets[i], FileMode.Create, FileAccess.Write, FileShare.None);
            content.CopyTo(file);
            bytes += entry.Size;
        }

        return new YpfUnpackResult(archive.Entries.Count, bytes);
    }

    /// <summary>
    /// Grava em <paramref name="output"/> o pacote reconstruído a partir de <paramref name="directory"/>. Arquivos
    /// iguais ao conteúdo do original não viram troca; entradas sem arquivo na pasta são mantidas. O pack não
    /// acrescenta entradas: um arquivo da pasta que o original não tem é recusado antes de gravar qualquer byte.
    /// </summary>
    /// <exception cref="VNForgeException">A pasta não existe, tem arquivo fora do pacote, ou o pacote não aceita trocas.</exception>
    public static YpfPackResult Pack(YpfArchive archive, string directory, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(output);

        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new VNForgeException($"A pasta não existe: {root}");
        }

        var known = archive.Entries.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (known.Contains(relative))
            {
                files[relative] = file;
            }
            else
            {
                unknown.Add(relative);
            }
        }

        if (unknown.Count > 0)
        {
            unknown.Sort(StringComparer.Ordinal);
            var shown = string.Join(", ", unknown.Take(5));
            var rest = unknown.Count > 5 ? $" e mais {unknown.Count - 5}" : string.Empty;
            throw new VNForgeException(
                $"A pasta tem arquivos que o YPF original não contém: {shown}{rest}. O pack não acrescenta entradas.");
        }

        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var replaced = new List<string>();
        var missing = new List<string>();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!files.TryGetValue(entry.Path, out var file))
            {
                missing.Add(entry.Path);
                continue;
            }

            var bytes = File.ReadAllBytes(file);
            if (!MatchesEntry(archive, entry, bytes))
            {
                replacements[entry.Path] = bytes;
                replaced.Add(entry.Path);
            }
        }

        try
        {
            archive.WriteTo(output, replacements);
        }
        catch (NotSupportedException ex)
        {
            throw new VNForgeException(ex.Message, ex);
        }

        return new YpfPackResult(archive.Entries.Count, replaced, missing);
    }

    private static bool MatchesEntry(YpfArchive archive, YpfEntry entry, byte[] bytes)
    {
        if (bytes.Length != entry.Size)
        {
            return false;
        }

        using var content = archive.OpenEntry(entry);
        var current = new byte[bytes.Length];
        content.ReadExactly(current);
        return current.AsSpan().SequenceEqual(bytes);
    }

    private static string[] ResolveTargets(YpfArchive archive, string root)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new Dictionary<string, string>(comparer);
        var directories = new HashSet<string>(comparer);
        var targets = new string[archive.Entries.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            var entryPath = archive.Entries[i].Path;
            if (!IsSafeRelativePath(entryPath))
            {
                throw new VNForgeException(
                    $"A entrada \"{entryPath}\" tem um caminho inválido ou que sai da pasta de destino; nada foi extraído.");
            }

            var target = Path.GetFullPath(Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix, comparison))
            {
                throw new VNForgeException(
                    $"A entrada \"{entryPath}\" tem um caminho que sai da pasta de destino; nada foi extraído.");
            }

            if (seen.TryGetValue(target, out var other))
            {
                throw new VNForgeException(
                    $"As entradas \"{other}\" e \"{entryPath}\" gravariam o mesmo arquivo neste sistema de arquivos; nada foi extraído.");
            }

            seen[target] = entryPath;
            targets[i] = target;
            for (var parent = Path.GetDirectoryName(target); parent is not null && parent.Length >= prefix.Length; parent = Path.GetDirectoryName(parent))
            {
                directories.Add(parent);
            }
        }

        foreach (var (target, entryPath) in seen)
        {
            if (directories.Contains(target))
            {
                throw new VNForgeException(
                    $"A entrada \"{entryPath}\" é um arquivo e também uma pasta de outra entrada; nada foi extraído.");
            }
        }

        return targets;
    }

    private static bool IsSafeRelativePath(string path)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.AsSpan().IndexOfAny(invalid) >= 0)
            {
                return false;
            }

            if (OperatingSystem.IsWindows() && (segment[^1] == '.' || segment[^1] == ' ' || IsReservedDeviceName(segment)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        var name = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        return ReservedDeviceNames.Contains(name);
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
}
