using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Yslb;
using VNForge.Engines.YuRis.Ystl;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Descompacta os pacotes YPF do jogo que carregam os scripts e abre o resultado (<see cref="IGameUnpacker"/>). Um pacote conta quando
/// tem algum <c>yst*.ybn</c>, o <c>ysc.ybn</c> ou o <c>yst_list.ybn</c>; os outros (imagens, áudio) ficam de fora, porque o texto não
/// está neles e eles pesam gigabytes. Quem precisa de um deles usa <c>vnf ypf unpack</c>.
/// </summary>
internal static class YuRisUnpacker
{
    internal const string CommandsFile = "ysc.ybn";
    internal const string ScriptListFile = "yst_list.ybn";
    internal const string LabelListFile = "ysl.ybn";
    internal const string ConfigFile = "yscfg.ybn";

    private static readonly YpfArchiveFormat Format = new();

    public static GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(gameDir);
        if (!Directory.Exists(root))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {root}");
        }

        var destination = Path.GetFullPath(destinationDirectory);
        var warnings = new List<string>();
        var archives = new List<UnpackedArchive>();
        foreach (var file in YuRisDetector.FindYpfFiles(root, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var archive = UnpackIfNeeded(file, relative, destination, warnings, cancellationToken);
            if (archive is not null)
            {
                archives.Add(archive);
            }
        }

        if (archives.Count == 0)
        {
            var hint = warnings.Count == 0 ? string.Empty : $" Avisos: {string.Join("; ", warnings)}.";
            throw new VNForgeException($"Nenhum pacote YPF com scripts (yst*.ybn) em '{root}'.{hint}");
        }

        return new GameUnpackResult(archives) { Warnings = warnings };
    }

    public static UnpackedGame Open(string unpackedDirectory, IScriptFormat scriptFormat)
    {
        var root = Path.GetFullPath(unpackedDirectory);
        if (!Directory.Exists(root))
        {
            throw new VNForgeException($"A pasta com o jogo descompactado não existe: {root}. Rode vnf extract.");
        }

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var commandsPath = files.FirstOrDefault(f => IsNamed(f, CommandsFile))
            ?? throw new VNForgeException($"O {CommandsFile} não está em '{root}'; sem ele não dá para ler os scripts. Rode vnf extract.");
        var warnings = new List<string>();
        var commands = YscmReader.ReadFile(commandsPath);
        var scriptList = ReadScriptList(files.FirstOrDefault(f => IsNamed(f, ScriptListFile)), warnings);
        var labels = ReadLabelList(files.FirstOrDefault(f => IsNamed(f, LabelListFile)), warnings);

        var scripts = files
            .Where(f => YuRisDetector.IsScriptName(Path.GetFileName(f)))
            .Select(f => new { Path = f, Name = Path.GetFileName(f) })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => new UnpackedScript(f.Name, f.Path, scriptFormat, NameOf(scriptList, f.Name)) { Folder = FolderOf(scriptList, f.Name) })
            .ToList();

        return new UnpackedGame(new YuRisContext(commands, scriptList, labels), scripts) { Warnings = warnings };
    }

    private static UnpackedArchive? UnpackIfNeeded(string file, string relative, string destination, List<string> warnings, CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (YuRisDetector.IsReadFailure(ex))
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
            catch (Exception ex) when (YuRisDetector.IsReadFailure(ex))
            {
                warnings.Add($"{relative}: {YuRisDetector.ReasonOf(ex)}");
                return null;
            }

            using (archive)
            {
                if (!archive.Entries.Any(IsWanted))
                {
                    return null;
                }

                var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                var unpacked = YpfFolder.Unpack(archive, target, cancellationToken);
                return new UnpackedArchive(relative, relative, unpacked.FileCount, unpacked.ByteCount);
            }
        }
    }

    private static bool IsWanted(ArchiveEntry entry)
    {
        var name = YuRisDetector.FileNameOf(entry);
        return YuRisDetector.IsScriptName(name)
            || name.Equals(CommandsFile, StringComparison.OrdinalIgnoreCase)
            || name.Equals(ScriptListFile, StringComparison.OrdinalIgnoreCase);
    }

    private static YstlFile? ReadScriptList(string? path, List<string> warnings)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return YstlReader.ReadFile(path);
        }
        catch (Exception ex) when (YuRisDetector.IsReadFailure(ex))
        {
            warnings.Add($"{ScriptListFile}: {YuRisDetector.ReasonOf(ex)}; os scripts ficam sem nome legível.");
            return null;
        }
    }

    private static YslbFile? ReadLabelList(string? path, List<string> warnings)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return YslbReader.ReadFile(path);
        }
        catch (Exception ex) when (YuRisDetector.IsReadFailure(ex))
        {
            warnings.Add($"{LabelListFile}: {YuRisDetector.ReasonOf(ex)}; o mapa de cenas fica sem saltos.");
            return null;
        }
    }

    private static string? NameOf(YstlFile? list, string fileName) =>
        list is not null && list.TryGetByBinaryName(fileName, out var script) ? script.ReadableName : null;

    private static string? FolderOf(YstlFile? list, string fileName) =>
        list is not null && list.TryGetByBinaryName(fileName, out var script) && script.Folder.Length > 0 ? script.Folder : null;

    private static bool IsNamed(string path, string name) =>
        string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);
}
