using System.Buffers.Binary;
using System.Globalization;
using System.Security;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Diagnóstico de uma pasta de jogo YU-RIS (<see cref="YuRisEngine.Detect"/>). Só abre arquivos para leitura e
/// converte qualquer falha de leitura em aviso, para a pasta de outra engine nunca gerar exceção.
/// </summary>
internal static class YuRisDetector
{
    private const int MaxYpfFiles = 64;
    private const int MaxScriptsTried = 8;
    private const string ScriptArchiveName = "ysbin.ypf";
    private const string CommandsEntry = "ysc.ybn";
    private const string PackedFolder = "pac";

    private static readonly YpfArchiveFormat Format = new();

    public static EngineDetection Detect(string gameDir)
    {
        string root;
        try
        {
            root = Path.GetFullPath(gameDir);
            if (!Directory.Exists(root))
            {
                return EngineDetection.NotDetected;
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return EngineDetection.NotDetected;
        }

        var warnings = new List<string>();
        var packages = new List<PackageInfo>();
        ScriptInfo? scripts = null;

        foreach (var file in FindYpfFiles(root))
        {
            var package = Inspect(root, file, warnings, ref scripts);
            if (package is not null)
            {
                packages.Add(package);
            }
        }

        return packages.Count == 0 ? EngineDetection.NotDetected : Report(packages, scripts, warnings);
    }

    private static PackageInfo? Inspect(string root, string file, List<string> warnings, ref ScriptInfo? scripts)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        FileStream stream;
        try
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            warnings.Add($"{relative}: não foi possível abrir ({ex.Message})");
            return null;
        }

        using (stream)
        {
            if (!Format.CanOpen(stream))
            {
                return null;
            }

            YpfArchive archive;
            try
            {
                archive = (YpfArchive)Format.Open(stream);
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                warnings.Add($"{relative}: {ReasonOf(ex)}");
                return new PackageInfo(relative, null, 0);
            }

            using (archive)
            {
                var package = new PackageInfo(relative, archive.Parameters.Version, archive.Entries.Count);
                if (scripts is null && HasScripts(archive))
                {
                    scripts = ReadScripts(archive, relative, warnings);
                }

                return package;
            }
        }
    }

    private static bool HasScripts(YpfArchive archive) =>
        archive.Entries.Any(e => IsScriptName(FileNameOf(e)));

    private static ScriptInfo ReadScripts(YpfArchive archive, string relative, List<string> warnings)
    {
        var scriptEntries = archive.Entries
            .Where(e => IsScriptName(FileNameOf(e)))
            .OrderBy(FileNameOf, StringComparer.OrdinalIgnoreCase)
            .ToList();

        YscmFile? commands = null;
        var commandsEntry = archive.Entries.FirstOrDefault(e => IsNamed(e, CommandsEntry));
        if (commandsEntry is null)
        {
            warnings.Add($"{CommandsEntry} não está em {relative}; sem ele não dá para ler a chave dos scripts.");
        }
        else
        {
            var data = ReadEntry(archive, commandsEntry, warnings);
            if (data is not null)
            {
                try
                {
                    commands = YscmReader.Read(data, CommandsEntry);
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    warnings.Add($"{CommandsEntry}: {ReasonOf(ex)}");
                }
            }
        }

        uint? ystbVersion = null;
        YstbFile? read = null;
        string? firstFailure = null;
        foreach (var entry in scriptEntries.Take(MaxScriptsTried))
        {
            var data = ReadEntry(archive, entry, warnings);
            if (data is null)
            {
                continue;
            }

            ystbVersion ??= PeekVersion(data);
            if (commands is null)
            {
                if (ystbVersion is not null)
                {
                    break;
                }

                continue;
            }

            try
            {
                read = YstbReader.Read(data, FileNameOf(entry), commands);
                break;
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                firstFailure ??= $"{FileNameOf(entry)}: {ReasonOf(ex)}";
            }
        }

        if (read is null && firstFailure is not null)
        {
            warnings.Add(firstFailure);
        }

        return new ScriptInfo(relative, archive.Parameters.Version, scriptEntries.Count, read?.Version ?? ystbVersion, read);
    }

    private static byte[]? ReadEntry(YpfArchive archive, ArchiveEntry entry, List<string> warnings)
    {
        if (entry.Size > YbnReader.MaxFileBytes || (entry is YpfEntry ypf && ypf.SizeStored > YbnReader.MaxFileBytes))
        {
            warnings.Add($"{FileNameOf(entry)}: passa do limite de {YbnReader.MaxFileBytes / (1024 * 1024)} MiB que o leitor aceita.");
            return null;
        }

        try
        {
            using var source = archive.OpenEntry(entry);
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            warnings.Add($"{FileNameOf(entry)}: não foi possível ler do pacote ({ReasonOf(ex)})");
            return null;
        }
    }

    private static uint? PeekVersion(byte[] data) =>
        data.Length >= 8 && data.AsSpan(0, 4).SequenceEqual("YSTB"u8)
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4))
            : null;

    private static EngineDetection Report(List<PackageInfo> packages, ScriptInfo? scripts, List<string> warnings)
    {
        var inv = CultureInfo.InvariantCulture;
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        var summary = new List<string>();

        foreach (var package in packages)
        {
            details[$"ypf:{package.Path}"] = package.Version is { } v
                ? $"v{v.ToString(inv)}, {package.Count.ToString(inv)} {(package.Count == 1 ? "arquivo" : "arquivos")}"
                : "assinatura YPF, índice ilegível";
        }

        if (scripts is null)
        {
            summary.Add("pacotes " + string.Join(", ", packages.Select(Describe)));
        }
        else
        {
            details["ypfScripts"] = scripts.Path;
            details["ypfVersao"] = scripts.YpfVersion.ToString(inv);
            details["scripts"] = scripts.Count.ToString(inv);
            summary.Add($"{scripts.Path} v{scripts.YpfVersion.ToString(inv)}");

            if (scripts.YstbVersion is { } ystb)
            {
                details["ystbVersao"] = ystb.ToString(inv);
                summary.Add($"YSTB v{ystb.ToString(inv)}");
            }

            if (scripts.File is { } file)
            {
                var key = $"0x{file.Key:X8}";
                details["chaveXor"] = key;
                details["chaveXorOrigem"] = KeyOrigin(file);
                summary.Add($"chave XOR {key}");
            }
        }

        if (warnings.Count > 0)
        {
            details["avisos"] = string.Join("; ", warnings);
        }

        return EngineDetection.Matched("parece YU-RIS, " + string.Join(", ", summary), details);
    }

    private static string Describe(PackageInfo package) =>
        package.Version is { } v ? $"{package.Path} v{v.ToString(CultureInfo.InvariantCulture)}" : package.Path;

    private static string KeyOrigin(YstbFile file) => file.KeyOrigin switch
    {
        YstbKeyOrigin.None => "scripts sem cifra (chave 0)",
        _ when YstbReader.KnownKeys.Contains(file.Key) => "chave padrão do YU-RIS",
        _ => "derivada do primeiro argumento do script",
    };

    private static IEnumerable<string> FindYpfFiles(string root)
    {
        var files = new List<string>();
        CollectYpf(root, files);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(Path.GetFileName(directory), PackedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    CollectYpf(directory, files);
                }
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
        }

        return files
            .OrderBy(f => string.Equals(Path.GetFileName(f), ScriptArchiveName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(MaxYpfFiles);
    }

    private static void CollectYpf(string directory, List<string> files)
    {
        try
        {
            files.AddRange(Directory.EnumerateFiles(directory)
                .Where(f => string.Equals(Path.GetExtension(f), ".ypf", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
        }
    }

    private static string FileNameOf(ArchiveEntry entry) => entry.Path[(entry.Path.LastIndexOf('/') + 1)..];

    private static bool IsNamed(ArchiveEntry entry, string name) =>
        string.Equals(FileNameOf(entry), name, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>yst</c> + dígitos + <c>.ybn</c>; o <c>yst_list.ybn</c> não é um script.</summary>
    private static bool IsScriptName(string name) =>
        name.Length > 7
        && name.StartsWith("yst", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(3, name.Length - 7).IndexOfAnyExceptInRange('0', '9') < 0;

    private static string ReasonOf(Exception ex) => ex is InvalidFileException invalid ? invalid.Reason : ex.Message;

    private static bool IsReadFailure(Exception ex) =>
        ex is VNForgeException or IOException or UnauthorizedAccessException or SecurityException or InvalidDataException
            or NotSupportedException or ArgumentException or EndOfStreamException;

    private sealed record PackageInfo(string Path, uint? Version, int Count);

    private sealed record ScriptInfo(string Path, uint YpfVersion, int Count, uint? YstbVersion, YstbFile? File);
}
