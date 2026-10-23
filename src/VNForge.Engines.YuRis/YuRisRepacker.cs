using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Devolve os scripts reconstruídos aos pacotes YPF do jogo (<see cref="IGameRepacker"/>). Percorre os mesmos pacotes que o
/// <see cref="YuRisUnpacker"/> descompacta, no mesmo caminho relativo; só o que tem pasta em <c>.cache/game</c> entra. Um pacote sem script
/// alterado é copiado idêntico. Num pacote alterado, <see cref="YpfArchive.WriteTo"/> copia byte a byte tudo o que não é troca e grava o
/// resultado direto em <c>build/</c>: o <c>.cache</c> original nunca é escrito e a pasta não precisa de arquivos extras.
/// </summary>
internal static class YuRisRepacker
{
    private static readonly YpfArchiveFormat Format = new();

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static GameRepackResult Repack(
        string gameDir,
        string unpackedDirectory,
        IReadOnlyList<RebuiltScript> rebuilt,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(gameDir);
        if (!Directory.Exists(root))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {root}");
        }

        var unpacked = Path.GetFullPath(unpackedDirectory);
        var output = Path.GetFullPath(outputDirectory);
        var warnings = new List<string>();
        var pending = rebuilt.ToList();
        var archives = new List<RepackedArchive>();
        foreach (var file in YuRisDetector.FindYpfFiles(root, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var folder = Path.Combine(unpacked, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var mine = pending.Where(r => IsInside(folder, r.Path)).ToList();
            pending.RemoveAll(mine.Contains);
            var target = Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            archives.Add(new RepackedArchive(relative, WriteArchive(file, relative, folder, mine, target)));
        }

        if (pending.Count > 0)
        {
            throw new VNForgeException(
                $"O script '{pending[0].Path}' não está em nenhum pacote descompactado de '{root}'. O .cache/ não é deste jogo; rode vnf extract.");
        }

        return new GameRepackResult(archives) { Warnings = warnings };
    }

    private static int WriteArchive(string source, string relative, string folder, List<RebuiltScript> scripts, string target)
    {
        if (scripts.Count == 0)
        {
            File.Copy(source, target, overwrite: true);
            return 0;
        }

        try
        {
            using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var archive = (YpfArchive)Format.Open(stream);
            var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var script in scripts)
            {
                var entry = FindEntry(archive, relative, folder, script);
                if (!ReadEntry(archive, entry).AsSpan().SequenceEqual(script.Original))
                {
                    throw new VNForgeException(
                        $"A entrada \"{entry.Path}\" de {relative} não é mais a que o vnf extract descompactou: o jogo mudou depois da extração. Rode vnf extract de novo.");
                }

                replacements[entry.Path] = script.Content;
            }

            using var output = new FileStream(target, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            archive.WriteTo(output, replacements);
            return replacements.Count;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível gravar {relative} em build/: {ex.Message}", ex);
        }
    }

    private static YpfEntry FindEntry(YpfArchive archive, string relative, string folder, RebuiltScript script)
    {
        var entryPath = Path.GetRelativePath(folder, script.Path).Replace(Path.DirectorySeparatorChar, '/');
        var matches = archive.Entries.Where(e => PathComparer.Equals(e.Path, entryPath)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new VNForgeException($"A entrada \"{entryPath}\" não existe em {relative}: o jogo mudou depois da extração. Rode vnf extract de novo."),
            _ => throw new VNForgeException(
                $"O YPF {relative} tem várias entradas que só diferem na caixa em \"{entryPath}\"; neste sistema de arquivos elas são o mesmo arquivo e não dá para reempacotar."),
        };
    }

    private static byte[] ReadEntry(YpfArchive archive, YpfEntry entry)
    {
        using var content = archive.OpenEntry(entry);
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsInside(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
