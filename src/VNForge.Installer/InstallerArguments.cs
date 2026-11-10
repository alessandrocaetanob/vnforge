namespace VNForge.Installer;

/// <summary>As opções da linha de comando do instalador.</summary>
/// <param name="Folder">A pasta do jogo dada em <c>--pasta</c>, já como o jogador a escreveu.</param>
/// <param name="Error">Uma opção mal escrita (<c>--pasta</c> sem caminho); o modo de console a mostra e sai com erro.</param>
public sealed record InstallerArguments(bool Help, bool Licenses, bool Silent, bool Uninstall, string? Folder, string? Error)
{
    /// <summary>Com <c>--silencioso</c>, <c>--ajuda</c> ou <c>--licencas</c> o instalador não abre janela.</summary>
    public bool UsesConsole => Help || Licenses || Silent || Error is not null;

    public static InstallerArguments Parse(IReadOnlyList<string> args)
    {
        string? folder = null;
        string? error = null;
        var index = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--pasta")
            {
                index = i;
            }
        }

        if (index >= 0)
        {
            if (index + 1 < args.Count)
            {
                folder = args[index + 1];
            }
            else
            {
                error = "--pasta precisa de um caminho.";
            }
        }

        return new InstallerArguments(
            Help: args.Contains("--ajuda") || args.Contains("-h") || args.Contains("-?"),
            Licenses: args.Contains("--licencas"),
            Silent: args.Contains("--silencioso"),
            Uninstall: args.Contains("--desinstalar"),
            folder,
            error);
    }
}
