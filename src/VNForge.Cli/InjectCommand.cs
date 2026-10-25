using System.CommandLine;
using VNForge.Core.Injection;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf inject</c>: faz o backup dos originais que o <c>build/</c> substitui e grava o build na pasta do jogo.</summary>
internal static class InjectCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("inject", "Faz backup dos originais e grava o build na pasta do jogo (desfaça com vnf restore).");
        command.SetAction(parseResult => Run(cli, parseResult));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var injector = new GameInjector();
        var plan = GameInjector.Plan(project);
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);

        if (plan.Files.Count > 0 && !json && cli.RichOutput)
        {
            cli.Ansi.Write(RunSummaries.InjectTable(plan));
        }
        else if (plan.Files.Count > 0 && !json)
        {
            output.WriteLine($"Jogo: {plan.GameDirectory}");
            foreach (var file in plan.Files)
            {
                output.WriteLine($"  {file.RelativePath} ({(file.ReplacesExisting ? "substitui o original" : "arquivo novo")})");
            }
        }

        if (plan.Files.Count > 0 && !ConfirmationPrompt.Confirm(parseResult, cli, $"Gravar {plan.Files.Count} arquivo(s) na pasta do jogo?"))
        {
            output.WriteLine("Operação cancelada. Nada foi gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        using var projectLock = ProjectLock.Acquire(project);
        var result = GameStateFile.Inject(project, injector, plan, GameTestKind.Translation, null, TimeProvider.System);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                backupDirectory = result.BackupDirectory,
                written = result.Written,
                unchanged = result.UnchangedCount,
            }));
            return ErrorHandler.OkExitCode;
        }

        if (result.BackupDirectory is null)
        {
            output.WriteLine($"Nada a injetar: os {result.UnchangedCount} arquivo(s) de build/ já estão iguais no jogo.");
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Backup: {result.BackupDirectory}");
        output.WriteLine($"Injetado: {result.Written.Count} arquivo(s) em {result.GameDirectory} (hashes conferidos)");
        return ErrorHandler.OkExitCode;
    }
}
