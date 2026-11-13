using System.CommandLine;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf game move</c>: troca a pasta do jogo do projeto quando o jogo muda de lugar.</summary>
internal static class GameCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("game", "Muda a pasta do jogo registrada no projeto.");
        command.Subcommands.Add(CreateMove(cli));
        return command;
    }

    private static Command CreateMove(VnfCli cli)
    {
        var game = new Option<string>("--game")
        {
            Description = "Pasta nova do jogo.",
            HelpName = "pasta",
            Required = true,
        };
        var command = new Command("move", "Aponta o projeto para a pasta nova do jogo, se for o mesmo jogo.");
        command.Options.Add(game);
        command.SetAction(parseResult => Move(cli, parseResult, parseResult.GetRequiredValue(game)));
        return command;
    }

    private static int Move(VnfCli cli, ParseResult parseResult, string folder)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var result = ProjectGameRelocator.Move(project, Path.GetFullPath(folder, cli.CurrentDirectory));
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                changed = result.Changed,
                checkedFiles = result.CheckedFiles,
                manifestsUpdated = result.ManifestsUpdated,
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(result.Changed
            ? $"Jogo movido para {result.GameDirectory}: {CliFormat.Count(result.CheckedFiles, "arquivo conferido", "arquivos conferidos")} e {CliFormat.Count(result.ManifestsUpdated, "backup atualizado", "backups atualizados")}. A pasta do jogo não foi alterada."
            : $"O projeto já aponta para {result.GameDirectory}. Nada foi gravado.");
        return ErrorHandler.OkExitCode;
    }
}
