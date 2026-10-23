using System.CommandLine;
using System.Globalization;
using VNForge.Core;
using VNForge.Core.Output;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Cli;

/// <summary><c>vnf ypf info|unpack|pack</c>: ferramentas de baixo nível sobre um arquivo YPF.</summary>
public static class YpfCommand
{

    private const string FileArgumentName = "arquivo";

    private static readonly YpfArchiveFormat Format = new();

    public static Command Create(VnfCli cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        var ypf = new Command("ypf", "Ferramentas de baixo nível para arquivos YPF.");
        ypf.Subcommands.Add(CreateInfo(cli));
        ypf.Subcommands.Add(CreateUnpack(cli));
        ypf.Subcommands.Add(CreatePack(cli));
        return ypf;
    }

    private static Command CreateInfo(VnfCli cli)
    {
        var file = new Argument<string>(FileArgumentName) { Description = "Arquivo .ypf." };
        var command = new Command("info", "Mostra versão, variante, hashes e a lista de arquivos de um YPF.");
        command.Arguments.Add(file);
        command.SetAction(parseResult =>
        {
            using var archive = OpenArchive(cli, parseResult.GetRequiredValue(file));
            Print(parseResult, cli, JsonData(archive), () => HumanInfo(archive));
            return 0;
        });
        return command;
    }

    private static Command CreateUnpack(VnfCli cli)
    {
        var file = new Argument<string>(FileArgumentName) { Description = "Arquivo .ypf." };
        var folder = new Argument<string>("pasta") { Description = "Pasta de destino." };
        var command = new Command("unpack", "Extrai todos os arquivos de um YPF para uma pasta, já descomprimidos.");
        command.Arguments.Add(file);
        command.Arguments.Add(folder);
        command.SetAction((parseResult, token) =>
        {
            var target = Resolve(cli, parseResult.GetRequiredValue(folder));
            if (!parseResult.GetValue(cli.Options.Yes) && HasEntries(target))
            {
                throw new UsageException($"A pasta de destino não está vazia: {target}. Use --yes para sobrescrever os arquivos que coincidirem.");
            }

            var source = Resolve(cli, parseResult.GetRequiredValue(file));
            using var archive = OpenArchive(cli, source);
            var result = YpfFolder.Unpack(archive, target, token);
            Print(
                parseResult,
                cli,
                new { archive = source, directory = target, fileCount = result.FileCount, byteCount = result.ByteCount },
                () => $"Extraídos {result.FileCount} arquivos ({result.ByteCount.ToString(CultureInfo.InvariantCulture)} bytes) em {target}");
            return Task.FromResult(0);
        });
        return command;
    }

    private static Command CreatePack(VnfCli cli)
    {
        var folder = new Argument<string>("pasta") { Description = "Pasta com os arquivos, como o unpack os gravou." };
        var original = new Option<string>("--original")
        {
            Description = "YPF original: fornece versão, ordem e as entradas que não mudaram.",
            HelpName = FileArgumentName,
            Required = true,
        };
        var output = new Option<string>("--output")
        {
            Description = "YPF novo a gravar. Nunca o original.",
            HelpName = FileArgumentName,
            Required = true,
        };
        var command = new Command("pack", "Remonta um YPF a partir de uma pasta e do YPF original, com reescrita mínima.");
        command.Arguments.Add(folder);
        command.Options.Add(original);
        command.Options.Add(output);
        command.SetAction((parseResult, token) =>
        {
            var source = Resolve(cli, parseResult.GetRequiredValue(original));
            var target = Resolve(cli, parseResult.GetRequiredValue(output));
            if (SamePath(source, target))
            {
                throw new UsageException("O arquivo de saída não pode ser o YPF original: grave num arquivo novo.");
            }

            if (File.Exists(target) && !parseResult.GetValue(cli.Options.Yes))
            {
                throw new UsageException($"O arquivo de saída já existe: {target}. Use --yes para sobrescrever.");
            }

            var sourceFolder = Resolve(cli, parseResult.GetRequiredValue(folder));
            if (target.StartsWith(sourceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new UsageException("O arquivo de saída não pode ficar dentro da pasta que está sendo empacotada.");
            }

            using var archive = OpenArchive(cli, source);
            var result = PackTo(archive, sourceFolder, target, parseResult.GetValue(cli.Options.Yes), token);
            var identical = FilesEqual(source, target);
            Print(
                parseResult,
                cli,
                new
                {
                    original = source,
                    output = target,
                    entryCount = result.EntryCount,
                    replaced = result.Replaced,
                    missing = result.Missing,
                    identical,
                },
                () => HumanPack(result, target, identical));
            return Task.FromResult(0);
        });
        return command;
    }

    private static bool HasEntries(string directory)
    {
        try
        {
            return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler a pasta \"{directory}\": {ex.Message}", ex);
        }
    }

    private static YpfPackResult PackTo(YpfArchive archive, string folder, string target, bool overwrite, CancellationToken token)
    {
        YpfFolder.ValidatePackFolder(archive, folder);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            YpfPackResult result;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                result = YpfFolder.Pack(archive, folder, stream, token);
            }

            File.Move(temporary, target, overwrite);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível gravar \"{target}\": {ex.Message}", ex);
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

    private static YpfArchive OpenArchive(VnfCli cli, string path)
    {
        var full = Resolve(cli, path);
        if (!File.Exists(full))
        {
            throw new VNForgeException($"O arquivo não existe: {full}");
        }

        FileStream stream;
        try
        {
            stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível abrir \"{full}\": {ex.Message}", ex);
        }

        try
        {
            return (YpfArchive)Format.Open(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static string Resolve(VnfCli cli, string path) => Path.GetFullPath(path, cli.CurrentDirectory);

    private static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool SamePath(string a, string b) => string.Equals(a, b, PathComparison);

    private static bool FilesEqual(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length)
        {
            return false;
        }

        using var left = File.OpenRead(a);
        using var right = File.OpenRead(b);
        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[81920];
        int read;
        while ((read = left.Read(leftBuffer)) > 0)
        {
            right.ReadExactly(rightBuffer, 0, read);
            if (!leftBuffer.AsSpan(0, read).SequenceEqual(rightBuffer.AsSpan(0, read)))
            {
                return false;
            }
        }

        return true;
    }

    private static void Print(ParseResult parseResult, VnfCli cli, object data, Func<string> human)
    {
        var writer = parseResult.InvocationConfiguration.Output;
        writer.WriteLine(parseResult.GetValue(cli.Options.Json) ? JsonEnvelope.Result(data) : human());
    }

    private static string Hex(uint value) => $"0x{value:X8}";

    private static object JsonData(YpfArchive archive)
    {
        var p = archive.Parameters;
        return new
        {
            version = archive.Header.Version,
            fileCount = archive.Header.FileCount,
            headerSize = archive.Header.HeaderSize,
            nameHash = p.NameHash.ToString(),
            dataHash = p.DataHash.ToString(),
            dataHashVerified = p.DataHashVerified,
            offsetBits = p.OffsetSize == YpfOffsetSize.Bits64 ? 64 : 32,
            swapTable = p.SwapTable.ToString(),
            nameKey = $"0x{p.NameKey:X2}",
            headerSizeIncludesHeader = p.HeaderSizeIncludesHeader,
            indexSize = p.IndexSize,
            fileLength = p.FileLength,
            gapCount = p.Gaps.Count,
            trailerLength = p.Trailer.Length,
            files = archive.Entries.Select(e => new
            {
                path = e.Path,
                size = e.Size,
                storedSize = e.SizeStored,
                compressed = e.IsCompressed,
                fileType = e.FileType,
                offset = e.Offset,
                nameHash = Hex(e.NameHash),
                dataHash = Hex(e.DataHash),
            }).ToList(),
        };
    }

    private static string HumanInfo(YpfArchive archive)
    {
        var p = archive.Parameters;
        var lines = new List<string>
        {
            $"Versão: {archive.Header.Version}",
            $"Arquivos: {archive.Header.FileCount}",
            $"Tamanho do arquivo: {p.FileLength.ToString(CultureInfo.InvariantCulture)} bytes",
            $"Hash do nome: {p.NameHash}",
            $"Hash dos dados: {p.DataHash}" + (p.DataHashVerified ? string.Empty : " (padrão da versão, não confirmado)"),
            $"Offset: {(p.OffsetSize == YpfOffsetSize.Bits64 ? 64 : 32)} bits",
            $"Tabela de troca: {p.SwapTable}",
            $"Chave do nome: 0x{p.NameKey:X2}",
            $"Tamanho do índice: {p.IndexSize} bytes",
            string.Empty,
            "Caminho | Tamanho | Armazenado | Zlib | Hash do nome | Hash dos dados",
        };
        lines.AddRange(archive.Entries.Select(e =>
            $"{e.Path} | {e.Size.ToString(CultureInfo.InvariantCulture)} | {e.SizeStored} | {(e.IsCompressed ? "sim" : "não")} | {Hex(e.NameHash)} | {Hex(e.DataHash)}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string HumanPack(YpfPackResult result, string target, bool identical)
    {
        var lines = new List<string>
        {
            $"Gravado {target} com {result.EntryCount} entradas.",
            $"Trocados {result.Replaced.Count} arquivos" + (result.Replaced.Count > 0 ? ":" : "."),
        };
        lines.AddRange(result.Replaced.Select(path => "  " + path));
        if (result.Missing.Count > 0)
        {
            lines.Add($"Sem arquivo na pasta, mantidos do original: {result.Missing.Count}");
            lines.AddRange(result.Missing.Select(path => "  " + path));
        }

        lines.Add(identical ? "O resultado é idêntico ao original." : "O resultado difere do original.");
        return string.Join(Environment.NewLine, lines);
    }
}
