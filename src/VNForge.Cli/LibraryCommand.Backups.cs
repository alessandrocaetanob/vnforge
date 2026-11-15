using System.CommandLine;
using VNForge.Core.Library;
using VNForge.Core.Logging;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf library backup-folder</c>: a pasta dos backups de todos os projetos, a mesma da aba Backups dos Ajustes.</summary>
internal static partial class LibraryCommand
{
    private static readonly Option<bool> BackupFolderClearOption = new("--clear")
    {
        Description = "Volta ao padrão: a pasta backup dentro de cada projeto.",
    };

    private static Command CreateBackupFolder(VnfCli cli)
    {
        var path = new Argument<string?>("pasta")
        {
            Description = "A pasta que guarda os backups, com uma subpasta por projeto. Sem ela, mostra a pasta atual.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("backup-folder", "Mostra ou muda a pasta dos backups. Os backups que já existem continuam onde estão.");
        command.Arguments.Add(path);
        command.Options.Add(BackupFolderClearOption);
        command.SetAction(parseResult => BackupFolder(cli, parseResult, parseResult.GetValue(path)));
        return command;
    }

    private static int BackupFolder(VnfCli cli, ParseResult parseResult, string? path)
    {
        var clear = parseResult.GetValue(BackupFolderClearOption);
        if (clear && path is not null)
        {
            throw new UsageException("--clear não leva uma pasta.");
        }

        var file = FileOf(cli);
        var (folder, previous, changed) = path is null && !clear
            ? Read(file.Load(), false)
            : file.Update(library =>
            {
                var before = library.BackupFolder;
                library.SetBackupFolder(clear ? null : Path.GetFullPath(path!, cli.CurrentDirectory));
                return Read(library, !string.Equals(before, library.BackupFolder, StringComparison.Ordinal));
            });

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { backupFolder = folder, previousFolders = previous, changed }));
            return ErrorHandler.OkExitCode;
        }

        var saved = changed ? " Gravado." : string.Empty;
        var where = folder ?? "o padrão, a pasta backup dentro de cada projeto";
        output.WriteLine($"Pasta dos backups: {where}.{saved}");
        foreach (var old in previous)
        {
            output.WriteLine($"Pasta anterior: {old} (os backups dela continuam na lista e na restauração)");
        }

        return ErrorHandler.OkExitCode;

        static (string? Folder, IReadOnlyList<string> Previous, bool Changed) Read(GameLibrary library, bool changed) =>
            (library.BackupFolder, library.PreviousBackupFolders, changed);
    }
}
