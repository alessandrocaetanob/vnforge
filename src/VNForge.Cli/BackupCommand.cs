using System.CommandLine;
using VNForge.Core.Injection;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf backup</c>: guarda os originais do jogo que o build troca e o projeto na pasta dos backups, sem tocar no jogo.</summary>
internal static class BackupCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("backup", "Guarda os originais do jogo que o build troca e o projeto na pasta dos backups. Não mexe no jogo.");
        command.SetAction(parseResult => Run(cli, parseResult));
        return command;
    }

    /// <summary>Passa ao projeto a pasta dos backups que os Ajustes e <c>vnf library backup-folder</c> guardam em <c>library.json</c>.</summary>
    /// <exception cref="LibraryException">O <c>library.json</c> está ilegível.</exception>
    internal static ProjectFolder WithUserBackups(this ProjectFolder project, VnfCli cli) =>
        project.WithBackups(new LibraryFile(cli.UserConfigDirectory).Load().BackupLocations);

    private static int Run(VnfCli cli, ParseResult parseResult)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory)).WithUserBackups(cli);
        var plan = GameInjector.PlanBackup(project);
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);

        using var projectLock = ProjectLock.Acquire(project);
        var result = new GameInjector().BackUpNow(project, plan);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                backupDirectory = result.BackupDirectory,
                game = plan.GameState.ToString(),
                gameFiles = result.GameFiles,
                gameBytes = result.GameBytes,
                projectFiles = result.ProjectFiles,
                projectBytes = result.ProjectBytes,
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Backup: {result.BackupDirectory}");
        output.WriteLine(plan.GameState switch
        {
            ManualBackupGame.Included => $"Jogo: {result.GameFiles} arquivo(s) original(is) ({GameInjector.Megabytes(result.GameBytes)})",
            ManualBackupGame.NoGame => "Jogo: não entrou, a pasta do jogo deste projeto não existe.",
            _ => "Jogo: não entrou, ainda não há build e não se sabe quais arquivos ele troca.",
        });
        output.WriteLine($"Projeto: {result.ProjectFiles} arquivo(s) ({GameInjector.Megabytes(result.ProjectBytes)})");
        return ErrorHandler.OkExitCode;
    }
}
