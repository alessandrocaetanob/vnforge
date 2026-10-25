namespace VNForge.Cli;

/// <summary>Uma seção do <c>vnf --help</c> e os comandos dela, na ordem em que aparecem.</summary>
public sealed record HelpSection(string Title, IReadOnlyList<string> Commands);

/// <summary>
/// Ponto único de registro dos subcomandos. Cada comando novo entra com uma linha em <see cref="Register"/>,
/// por exemplo <c>cli.Root.Subcommands.Add(ExtractCommand.Create(cli));</c>, para que PRs paralelos
/// toquem linhas diferentes e não conflitem. O comando também entra em uma seção de <see cref="Sections"/>,
/// senão a ajuda o lista em "Outros comandos".
/// </summary>
public static class VnfCommands
{
    /// <summary>Seções do <c>vnf --help</c>: o pipeline na ordem de uso, os comandos do projeto e as ferramentas de baixo nível.</summary>
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("Pipeline", ["init", "extract", "translate", "review", "check", "build", "inject", "run", "restore", "test"]),
        new("Projeto", ["status", "preview", "export", "import", "glossary", "chars", "config", "detect"]),
        new("Baixo nível", ["ypf", "ybn", "patch-exe"]),
    ];

    public static void Register(VnfCli cli)
    {
        ArgumentNullException.ThrowIfNull(cli);
        cli.Root.Subcommands.Add(InitCommand.Create(cli));
        cli.Root.Subcommands.Add(ExtractCommand.Create(cli));
        cli.Root.Subcommands.Add(TranslateCommand.Create(cli));
        cli.Root.Subcommands.Add(ReviewCommand.Create(cli));
        cli.Root.Subcommands.Add(CheckCommand.Create(cli));
        cli.Root.Subcommands.Add(BuildCommand.Create(cli));
        cli.Root.Subcommands.Add(InjectCommand.Create(cli));
        cli.Root.Subcommands.Add(RunCommand.Create(cli));
        cli.Root.Subcommands.Add(RestoreCommand.Create(cli));
        cli.Root.Subcommands.Add(TestCommand.Create(cli));
        cli.Root.Subcommands.Add(StatusCommand.Create(cli));
        cli.Root.Subcommands.Add(PreviewCommand.Create(cli));
        cli.Root.Subcommands.Add(ExportCommand.Create(cli));
        cli.Root.Subcommands.Add(ImportCommand.Create(cli));
        cli.Root.Subcommands.Add(GlossaryCommand.Create(cli));
        cli.Root.Subcommands.Add(CharsCommand.Create(cli));
        cli.Root.Subcommands.Add(ConfigCommand.Create(cli));
        cli.Root.Subcommands.Add(DetectCommand.Create(cli));
        cli.Root.Subcommands.Add(YpfCommand.Create(cli));
        cli.Root.Subcommands.Add(YbnCommand.Create(cli));
        cli.Root.Subcommands.Add(PatchExeCommand.Create(cli));
    }
}
