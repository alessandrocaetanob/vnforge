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
    }
}
