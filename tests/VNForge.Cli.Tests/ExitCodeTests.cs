using System.Text.Json;
using VNForge.Core;
using VNForge.Core.Logging;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R6")]
public sealed class ExitCodeTests : IDisposable
{
    private const string Falha = "falha";
    private const string Cancela = "cancela";
    private const string NaoExisteOption = "--nao-existe";
    private const string Error = "error";
    private const string Importa = "importa";
    private const string GameOption = "--game";

    private const string FakeQaCommand = "fake-qa";
    private const string JsonFlag = "--json";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Comando_que_termina_bem_sai_com_0()
    {
        _cli.AddCommand("ok", _ => ErrorHandler.OkExitCode);

        var result = await _cli.RunAsync(["ok"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task Erro_esperado_sai_com_1_e_mensagem_curta_no_stderr()
    {
        _cli.AddCommand(Falha, _ => throw new VNForge.Core.VNForgeException("O arquivo 'a.ypf' é inválido: truncado"));

        var result = await _cli.RunAsync([Falha], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Erro: O arquivo 'a.ypf' é inválido: truncado" + Environment.NewLine, result.Error);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task QA_bloqueante_devolvido_pelo_comando_sai_com_2()
    {
        _cli.AddCommand(FakeQaCommand, _ => ErrorHandler.QaBlockedExitCode);

        var result = await _cli.RunAsync([FakeQaCommand], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task Cancelamento_lancado_pelo_comando_sai_com_3()
    {
        _cli.AddCommand(Cancela, _ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _cli.RunAsync([Cancela], cts.Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("Operação cancelada." + Environment.NewLine, result.Error);
    }

    private void AddCommandWithParameters()
    {
        var command = new System.CommandLine.Command(Importa, "Comando de teste.");
        command.Arguments.Add(new System.CommandLine.Argument<string>("arquivo"));
        command.Options.Add(new System.CommandLine.Option<string>(GameOption) { Required = true });
        command.Options.Add(new System.CommandLine.Option<int>("--limit"));
        command.SetAction(_ => 0);
        _cli.Cli.Root.Subcommands.Add(command);
    }
}
