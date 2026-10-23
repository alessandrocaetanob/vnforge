namespace VNForge.Cli;

/// <summary>
/// Ponto único de registro dos subcomandos. Cada comando novo entra com uma linha em <see cref="Register"/>,
/// por exemplo <c>cli.Root.Subcommands.Add(ExtractCommand.Create(cli));</c>, para que PRs paralelos
/// toquem linhas diferentes e não conflitem.
/// </summary>
public static class VnfCommands
{
    public static void Register(VnfCli cli)
    {
        ArgumentNullException.ThrowIfNull(cli);
        cli.Root.Subcommands.Add(DetectCommand.Create(cli));
        cli.Root.Subcommands.Add(YpfCommand.Create(cli));
        cli.Root.Subcommands.Add(InitCommand.Create(cli));
        cli.Root.Subcommands.Add(InjectCommand.Create(cli));
        cli.Root.Subcommands.Add(RestoreCommand.Create(cli));
        cli.Root.Subcommands.Add(YbnCommand.Create(cli));
        cli.Root.Subcommands.Add(ExtractCommand.Create(cli));
        cli.Root.Subcommands.Add(BuildCommand.Create(cli));
        cli.Root.Subcommands.Add(ExportCommand.Create(cli));
        cli.Root.Subcommands.Add(ImportCommand.Create(cli));
        cli.Root.Subcommands.Add(StatusCommand.Create(cli));
        cli.Root.Subcommands.Add(PreviewCommand.Create(cli));
        cli.Root.Subcommands.Add(ConfigCommand.Create(cli));
        cli.Root.Subcommands.Add(TranslateCommand.Create(cli));
        cli.Root.Subcommands.Add(ReviewCommand.Create(cli));
        cli.Root.Subcommands.Add(CheckCommand.Create(cli));
        cli.Root.Subcommands.Add(RunCommand.Create(cli));
    }
}
