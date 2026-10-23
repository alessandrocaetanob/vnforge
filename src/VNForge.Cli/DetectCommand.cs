using System.CommandLine;
using VNForge.Core.Engines;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Engines.YuRis;

namespace VNForge.Cli;

/// <summary><c>vnf detect [--game &lt;dir&gt;]</c>: diagnostica a engine do jogo, só lendo a pasta.</summary>
internal static class DetectCommand
{
    /// <summary>Engines que o <c>vnf</c> sabe reconhecer, na ordem em que são tentadas.</summary>
    private static readonly IEngine[] Engines = [new YuRisEngine()];

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
        foreach (var engine in Engines)
        {
            var result = engine.Detect(folder);
            if (result.IsMatch)
            {
                engineId = engine.Id;
                detection = result;
                break;
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
                details = detection?.Details,
            }));
            return ErrorHandler.OkExitCode;
        }

        if (detection is null)
        {
            output.WriteLine($"Engine não reconhecida na pasta {folder}.");
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Engine detectada: {engineId}");
        output.WriteLine(detection.Summary);
        foreach (var (name, value) in detection.Details)
        {
            output.WriteLine($"  {name}: {value}");
        }

        return ErrorHandler.OkExitCode;
    }
}
