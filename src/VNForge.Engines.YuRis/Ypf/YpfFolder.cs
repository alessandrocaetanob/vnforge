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
    /// Confere os caminhos e os tamanhos pelo índice antes de gravar o primeiro byte; arquivos que já existem são sobrescritos.
    /// Um erro de conteúdo ou de gravação no meio da extração (hash divergente, disco cheio) deixa a extração parcial.
    /// </summary>
    /// <exception cref="VNForgeException">
    /// Um caminho interno sai da pasta, é inválido, colide com outro, passa por link simbólico ou junção, uma entrada é
    /// grande demais, o conteúdo de uma entrada está corrompido, ou a gravação falhou.
    /// </exception>
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
            using var content = OpenEntry(archive, entry);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targets[i])!);
                using var file = new FileStream(targets[i], FileMode.Create, FileAccess.Write, FileShare.None);
                content.CopyTo(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new VNForgeException($"Não foi possível gravar \"{targets[i]}\" (entrada \"{entry.Path}\"): {ex.Message}", ex);
            }

            bytes += entry.Size;
        }

        return new YpfUnpackResult(archive.Entries.Count, bytes);
    }

    /// <summary>
    /// Confere, sem gravar nada, se <paramref name="directory"/> pode ser empacotada: a pasta existe e não tem arquivo
    /// que o original não contém. Devolve o arquivo da pasta de cada entrada, pelo caminho da entrada.
    /// </summary>
    /// <exception cref="VNForgeException">A pasta não existe, tem arquivo fora do pacote, ou o original tem duas entradas que só diferem na caixa.</exception>
    public static IReadOnlyDictionary<string, string> ValidatePackFolder(YpfArchive archive, string directory)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new VNForgeException($"A pasta não existe: {root}");
        }

        var known = new Dictionary<string, string>(PathComparer);
        foreach (var entry in archive.Entries)
        {
            if (!known.TryAdd(entry.Path, entry.Path))
            {
                throw new VNForgeException(
                    $"O YPF tem as entradas \"{known[entry.Path]}\" e \"{entry.Path}\", que só diferem na caixa; neste sistema de arquivos elas são o mesmo arquivo e não dá para empacotar.");
            }
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (known.TryGetValue(relative, out var entryPath))
                {
                    files[entryPath] = file;
                }
                else
                {
                    unknown.Add(relative);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler a pasta \"{root}\": {ex.Message}", ex);
        }

        if (unknown.Count > 0)
        {
            unknown.Sort(StringComparer.Ordinal);
            var shown = string.Join(", ", unknown.Take(5).Select(u => $"\"{u}\""));
            var rest = unknown.Count > 5 ? $" e mais {unknown.Count - 5}" : string.Empty;
            throw new VNForgeException(
                $"A pasta tem arquivos que o YPF original não contém: {shown}{rest}. O pack não acrescenta entradas: remova esses arquivos da pasta (por exemplo Thumbs.db ou desktop.ini) e tente de novo.");
        }

        return files;
    }

    /// <summary>
    /// Grava em <paramref name="output"/> o pacote reconstruído a partir de <paramref name="directory"/>. Arquivos
    /// iguais ao conteúdo do original não viram troca; entradas sem arquivo na pasta são mantidas. O pack não
    /// acrescenta entradas: um arquivo da pasta que o original não tem é recusado antes de gravar qualquer byte.
    /// No Windows os caminhos são comparados sem diferenciar caixa.
    /// </summary>
    /// <exception cref="VNForgeException">
    /// A pasta é inválida (ver <see cref="ValidatePackFolder"/>), o conteúdo do original está corrompido, a leitura
    /// ou a gravação falhou, ou o pacote não aceita trocas.
    /// </exception>
    public static YpfPackResult Pack(YpfArchive archive, string directory, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(output);

        var files = ValidatePackFolder(archive, directory);

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

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new VNForgeException($"Não foi possível ler \"{file}\": {ex.Message}", ex);
            }

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
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException(
                ex is NotSupportedException ? ex.Message : $"Não foi possível gravar o YPF novo: {ex.Message}",
                ex);
        }

        return new YpfPackResult(archive.Entries.Count, replaced, missing);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static Stream OpenEntry(YpfArchive archive, YpfEntry entry)
    {
        try
        {
            return archive.OpenEntry(entry);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
        {
            throw new VNForgeException($"Não foi possível ler a entrada \"{entry.Path}\" do YPF: {ex.Message}", ex);
        }
    }

    private static bool MatchesEntry(YpfArchive archive, YpfEntry entry, byte[] bytes)
    {
        if (bytes.Length != entry.Size)
        {
            return false;
        }

        using var content = OpenEntry(archive, entry);
        var current = new byte[bytes.Length];
        try
        {
            content.ReadExactly(current);
        }
        catch (EndOfStreamException ex)
        {
            throw new VNForgeException($"A entrada \"{entry.Path}\" do YPF termina antes do tamanho que o índice indica.", ex);
        }

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
            if (archive.Entries[i].Size > Array.MaxLength || archive.Entries[i].SizeStored > Array.MaxLength)
            {
                throw new VNForgeException(
                    $"A entrada \"{entryPath}\" é grande demais para extrair ({archive.Entries[i].Size} bytes); nada foi extraído.");
            }

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

            ThrowIfLinked(target, prefix, entryPath);
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

    private static void ThrowIfLinked(string target, string prefix, string entryPath)
    {
        for (var current = target; current is not null && current.Length >= prefix.Length; current = Path.GetDirectoryName(current))
        {
            var attributes = new FileInfo(current).Attributes;
            if ((int)attributes != -1 && attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new VNForgeException(
                    $"O caminho \"{current}\", usado pela entrada \"{entryPath}\", é um link simbólico ou junção; o unpack não segue links. Nada foi extraído.");
            }
        }
    }

    // Os nomes do YPF são caminhos do Windows: a validação é a mesma em qualquer sistema, para o mesmo YPF
    // ser aceito ou recusado igual no Linux e no Windows.
    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '|', '?', '*', '\\'];

    private static bool IsSafeRelativePath(string path)
    {
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment.AsSpan().IndexOfAny(InvalidNameChars) >= 0
                || segment.Any(char.IsControl)
                || segment[^1] == '.'
                || segment[^1] == ' '
                || IsReservedDeviceName(segment))
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
