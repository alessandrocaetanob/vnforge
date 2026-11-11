using Spectre.Console;
using VNForge.Cli;

namespace VNForge.Cli.Tests;

public sealed class RichOutputTests : IDisposable
{
    private const string Escape = "\u001b[";
    private const string ApprovedTrueColor = "38;2;92;203;138";
    private const string Saida = "saida";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static GlobalSettings Settings(bool json = false, bool quiet = false, bool noColor = false) =>
        new(null, json, false, quiet, false, noColor);

    [Fact]
    public async Task Writer_que_nao_e_o_console_do_processo_nunca_e_interativo()
    {
        using var plain = new CliHarness();
        plain.AddCommand(Saida, _ => 0);

        await plain.RunAsync([Saida], TestContext.Current.CancellationToken);

        Assert.False(CliTerminal.ForWriter(new StringWriter()).IsInteractive);
        Assert.False(plain.Cli.RichOutput);
    }

}
