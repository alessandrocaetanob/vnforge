using System.CommandLine;
using System.Globalization;
using System.Text;
using VNForge.Core;
using VNForge.Core.Output;
using VNForge.Engines.YuRis.Yscm;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Cli;

/// <summary><c>vnf ybn dump</c>: ferramentas de baixo nível sobre arquivos <c>.ybn</c> do YU-RIS. Só leem.</summary>
public static class YbnCommand
{
    private const string YscName = "ysc.ybn";

    public static Command Create(VnfCli cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        var ybn = new Command("ybn", "Ferramentas de baixo nível para arquivos .ybn do YU-RIS.");
        ybn.Subcommands.Add(CreateDump(cli));
        return ybn;
    }

    private static Command CreateDump(VnfCli cli)
    {
        var file = new Argument<string>("arquivo") { Description = "Script yst*.ybn (YSTB)." };
        var ysc = new Option<string?>("--ysc")
        {
            Description = $"{YscName} do mesmo jogo, que dá o nome dos comandos. Sem a opção, procura na pasta do script e na pasta acima.",
            HelpName = "arquivo",
        };
        var command = new Command("dump", "Despeja um script YSTB legível: comandos, argumentos e textos com marcadores. Não grava nada.");
        command.Arguments.Add(file);
        command.Options.Add(ysc);
        command.SetAction(parseResult =>
        {
            var scriptPath = ExistingFile(cli, parseResult.GetRequiredValue(file));
            var yscPath = parseResult.GetValue(ysc) is { } given ? ExistingFile(cli, given) : FindYsc(scriptPath);

            var commands = Read(yscPath, YscmReader.ReadFile);
            var script = Read(scriptPath, path => YstbReader.ReadFile(path, commands));
            var dump = YstbDumper.Dump(script, commands);

            var writer = parseResult.InvocationConfiguration.Output;
            writer.WriteLine(parseResult.GetValue(cli.Options.Json) ? JsonEnvelope.Result(JsonData(dump, yscPath)) : Human(dump, yscPath));
            return 0;
        });
        return command;
    }

    private static string ExistingFile(VnfCli cli, string path)
    {
        var full = Path.GetFullPath(path, cli.CurrentDirectory);
        return File.Exists(full) ? full : throw new VNForgeException($"O arquivo não existe: {full}");
    }

    private static string FindYsc(string scriptPath)
    {
        var folder = Path.GetDirectoryName(scriptPath) ?? ".";
        var candidates = new List<string> { Path.Combine(folder, YscName) };
        if (Path.GetDirectoryName(folder) is { } parent)
        {
            candidates.Add(Path.Combine(parent, YscName));
        }

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new VNForgeException(
                $"Não achei o {YscName} na pasta do script nem na pasta acima ({string.Join(", ", candidates.Select(Path.GetDirectoryName))}). Informe o caminho com --ysc.");
    }

    private static T Read<T>(string path, Func<string, T> read)
    {
        try
        {
            return read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VNForgeException($"Não foi possível ler \"{path}\": {ex.Message}", ex);
        }
    }

    private static string Key(uint key) => $"0x{key:X8}";

    private static string KeyOrigin(YstbKeyOrigin origin) => origin switch
    {
        YstbKeyOrigin.None => "none",
        YstbKeyOrigin.Derived => "derived",
        _ => "known",
    };

    private static string KeyOriginText(YstbKeyOrigin origin) => origin switch
    {
        YstbKeyOrigin.None => "seções em claro",
        YstbKeyOrigin.Derived => "derivada do arquivo",
        _ => "chave conhecida",
    };

    private static string KindName(YstbArgumentKind kind) => kind switch
    {
        YstbArgumentKind.Text => "text",
        YstbArgumentKind.String => "string",
        YstbArgumentKind.Expression => "expression",
        YstbArgumentKind.Value => "value",
        _ => "empty",
    };

    private static string KindText(YstbArgumentKind kind) => kind switch
    {
        YstbArgumentKind.Text => "texto",
        YstbArgumentKind.String => "string",
        YstbArgumentKind.Expression => "expressão",
        YstbArgumentKind.Value => "valor",
        _ => "vazio",
    };

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static object JsonData(YstbDump dump, string yscPath) => new
    {
        file = dump.Source,
        ysc = yscPath,
        version = dump.Version,
        key = Key(dump.Key),
        keyOrigin = KeyOrigin(dump.KeyOrigin),
        instructionCount = dump.Instructions.Count,
        argumentCount = dump.ArgumentCount,
        instructions = dump.Instructions.Select(i => new
        {
            index = i.Index,
            line = i.LineNumber,
            opcode = i.Opcode,
            command = i.Command,
            extra = i.Extra,
            arguments = i.Arguments.Select(a => new
            {
                index = a.Index,
                id = a.Id,
                name = a.Name,
                type = a.Type,
                assignOp = a.AssignOp,
                assignOperator = a.AssignOperator,
                size = a.Size,
                offset = a.Offset,
                kind = KindName(a.Kind),
                value = a.Value,
            }).ToList(),
        }).ToList(),
    };

    private static string Human(YstbDump dump, string yscPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Arquivo: {dump.Source}");
        sb.AppendLine($"YSCM: {yscPath}");
        sb.AppendLine($"Versão: {Number(dump.Version)}");
        sb.AppendLine($"Chave: {Key(dump.Key)} ({KeyOriginText(dump.KeyOrigin)})");
        sb.AppendLine($"Instruções: {Number(dump.Instructions.Count)}");
        sb.Append($"Argumentos: {Number(dump.ArgumentCount)}");
        foreach (var instruction in dump.Instructions)
        {
            sb.AppendLine().AppendLine();
            sb.Append($"#{instruction.Index.ToString("D4", CultureInfo.InvariantCulture)}  linha {Number(instruction.LineNumber)}  {instruction.Command}");
            foreach (var argument in instruction.Arguments)
            {
                sb.AppendLine().Append(HumanArgument(argument));
            }
        }

        return sb.ToString();
    }

    private static string HumanArgument(YstbDumpArgument argument)
    {
        var name = argument.Name ?? $"#{Number(argument.Id)}";
        var head = $"    [{Number(argument.Index)}] {name} ({KindText(argument.Kind)})";
        return argument.Kind switch
        {
            YstbArgumentKind.Empty => argument.AssignOp == 0 ? head : $"{head} {argument.AssignOperator}",
            YstbArgumentKind.Value => $"{head} {argument.AssignOperator} tamanho {Number(argument.Size)}, offset {Number(argument.Offset)}",
            _ => $"{head} {argument.AssignOperator} {argument.Value}",
        };
    }
}
