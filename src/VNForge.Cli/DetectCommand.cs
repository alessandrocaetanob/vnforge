using System.Collections.Frozen;
using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Engines;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf detect [--game &lt;dir&gt;]</c>: diagnostica a engine do jogo, só lendo a pasta.</summary>
internal static class DetectCommand
{
    private const string PackagePrefix = "ypf:";

    public static Command Create(VnfCli cli)
    {
        var game = new Option<string?>("--game")
        {
            Description = "Pasta do jogo (padrão: a pasta atual).",
            HelpName = "dir",
        };

        var command = new Command("detect", "Diagnostica a engine do jogo (versões, arquivos de pacote, chave XOR) sem gravar nada.");
        command.Options.Add(game);
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.GetValue(game)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, string? gameOption)
    {
        var folder = Path.GetFullPath(string.IsNullOrWhiteSpace(gameOption) ? "." : gameOption, cli.CurrentDirectory);
        if (!Directory.Exists(folder))
        {
            throw new UsageException($"A pasta do jogo não existe: {folder}");
        }

        string? engineId = null;
        EngineDetection? detection = null;
        IReadOnlyDictionary<string, string>? details = null;
        foreach (var engine in KnownEngines.All)
        {
            var result = engine.Detect(folder);
            if (result.IsMatch)
            {
                engineId = engine.Id;
                detection = result;
                details = result.Details;
                break;
            }

            if (details is null && result.Details.Count > 0)
            {
                details = result.Details;
            }
        }

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                detected = detection is not null,
                folder,
                engine = engineId,
                summary = detection?.Summary,
                details,
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteTree(cli.Ansi, folder, engineId, detection, details ?? FrozenDictionary<string, string>.Empty);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteTree(IAnsiConsole ansi, string folder, string? engineId, EngineDetection? detection, IReadOnlyDictionary<string, string> details)
    {
        var accent = CliPalette.Hex(CliPalette.Accent);
        var root = new Tree(new Markup(detection is null
            ? $"[{CliPalette.Hex(CliPalette.Warning)}]Engine não reconhecida[/] na pasta {CliViews.Escape(folder)}."
            : $"Engine detectada: [bold {accent}]{CliViews.Escape(engineId ?? string.Empty)}[/]"));

        if (detection is not null)
        {
            root.AddNode(CliViews.Escape(detection.Summary));
        }

        TreeNode? packages = null;
        foreach (var (name, value) in details)
        {
            if (name.StartsWith(PackagePrefix, StringComparison.Ordinal))
            {
                packages ??= root.AddNode($"[{accent}]Pacotes[/]");
                packages.AddNode($"{CliViews.Escape(name[PackagePrefix.Length..])}: {CliViews.Escape(value)}");
                continue;
            }

            var color = name == "warnings" ? $"[{CliPalette.Hex(CliPalette.Warning)}]" : $"[{accent}]";
            root.AddNode($"{color}{CliViews.Escape(LabelOf(name))}[/]: {CliViews.Escape(value)}");
        }

        ansi.Write(root);
    }

    private static string LabelOf(string key)
    {
        if (key.StartsWith(PackagePrefix, StringComparison.Ordinal))
        {
            return "Pacote " + key[PackagePrefix.Length..];
        }

        return key switch
        {
            "scriptsArchive" => "Pacote de scripts",
            "ypfVersion" => "Versão do YPF",
            "scripts" => "Scripts",
            "ystbVersion" => "Versão do YSTB",
            "scriptKey" => "Chave XOR",
            "scriptKeyOrigin" => "Origem da chave",
            "warnings" => "Avisos",
            _ => key,
        };
    }
}
