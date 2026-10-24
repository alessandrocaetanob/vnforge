using System.CommandLine;
using VNForge.Core.Injection;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf restore</c>: devolve à pasta do jogo os originais do backup mais recente.</summary>
internal static class RestoreCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("restore", "Devolve à pasta do jogo os arquivos originais do backup mais recente.");
        command.SetAction(parseResult => Run(cli, parseResult));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var injector = new GameInjector();
        var plan = GameInjector.PlanRestore(project);
        var output = parseResult.InvocationConfiguration.Output;
        var json = parseResult.GetValue(cli.Options.Json);

        if (!json)
        {
            output.WriteLine($"Backup: {plan.BackupDirectory}");
            output.WriteLine($"Jogo: {plan.GameDirectory}");
        }

        if (!ConfirmationPrompt.Confirm(parseResult, cli, $"Restaurar {plan.Manifest.Files.Count} arquivo(s) do backup?"))
        {
            output.WriteLine("Operação cancelada. Nada foi gravado.");
            return ErrorHandler.CancelledExitCode;
        }

        var result = injector.Restore(plan);
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                backupDirectory = result.BackupDirectory,
                restored = result.Restored,
                removed = result.Removed,
                kept = result.Kept,
                warnings = result.Warnings,
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Restaurado: {result.Restored.Count} arquivo(s); removido: {result.Removed.Count} arquivo(s) criado(s) pela injeção.");
        foreach (var kept in result.Kept)
        {
            output.WriteLine($"Mantido: {kept} (criado pela injeção, mas alterado depois; apague à mão se quiser)");
        }

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"Aviso: {warning}");
        }

        return ErrorHandler.OkExitCode;
    }
}
