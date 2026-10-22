using System.Text.Json;
using VNForge.Core;
using VNForge.Core.Logging;

namespace VNForge.Cli.Tests;

public sealed class ExitCodeTests : IDisposable
{
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
        _cli.AddCommand("falha", _ => throw new InvalidFileException("a.ypf", "truncado"));

        var result = await _cli.RunAsync(["falha"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Erro: O arquivo 'a.ypf' é inválido: truncado" + Environment.NewLine, result.Error);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public async Task Erro_inesperado_sai_com_1_sem_stack_trace()
    {
        _cli.AddCommand("bug", _ => throw new InvalidOperationException("estado quebrado"));

        var result = await _cli.RunAsync(["bug"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Erro inesperado.", result.Error);
        Assert.DoesNotContain("estado quebrado", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    [Fact]
    public async Task QA_bloqueante_devolvido_pelo_comando_sai_com_2()
    {
        _cli.AddCommand("check", _ => ErrorHandler.QaBlockedExitCode);

        var result = await _cli.RunAsync(["check"], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task Cancelamento_lancado_pelo_comando_sai_com_3()
    {
        _cli.AddCommand("cancela", _ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _cli.RunAsync(["cancela"], cts.Token);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("Operação cancelada." + Environment.NewLine, result.Error);
    }

    [Fact]
    public async Task Timeout_de_rede_com_o_token_ativo_e_erro_e_nao_cancelamento()
    {
        _cli.AddCommand("rede", _ => throw new TaskCanceledException("HttpClient.Timeout"));

        var result = await _cli.RunAsync(["rede"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Erro inesperado.", result.Error);
    }

    [Fact]
    public async Task Cancelar_o_token_com_o_comando_em_andamento_sai_com_3()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cli.AddCommand("longo", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var running = _cli.RunAsync(["longo"], cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        var result = await running;

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("Operação cancelada." + Environment.NewLine, result.Error);
    }

    [Fact]
    public async Task Opcao_desconhecida_sai_com_1_e_aponta_a_ajuda()
    {
        var result = await _cli.RunAsync(["--nao-existe"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Erro: ", result.Error);
        Assert.Contains("--nao-existe", result.Error);
        Assert.Contains("--help", result.Error);
    }

    [Fact]
    public async Task Verbose_e_quiet_juntos_sao_erro_de_uso()
    {
        var result = await _cli.RunAsync(["--verbose", "--quiet"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--verbose", result.Error);
        Assert.Contains("--quiet", result.Error);
    }

    [Fact]
    public async Task Json_nao_muda_o_codigo_de_saida_nem_escreve_no_stderr()
    {
        _cli.AddCommand("check", _ => ErrorHandler.QaBlockedExitCode);
        _cli.AddCommand("falha", _ => throw new InvalidFileException("a.ypf", "truncado"));
        _cli.AddCommand("cancela", _ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var qa = await _cli.RunAsync(["check", "--json"], TestContext.Current.CancellationToken);
        var erro = await _cli.RunAsync(["falha", "--json"], TestContext.Current.CancellationToken);
        var cancelado = await _cli.RunAsync(["cancela", "--json"], cts.Token);

        Assert.Equal(2, qa.ExitCode);
        Assert.Equal(1, erro.ExitCode);
        Assert.Equal(3, cancelado.ExitCode);
        Assert.All(new[] { qa, erro, cancelado }, r => Assert.Equal(string.Empty, r.Error));

        using var doc = JsonDocument.Parse(erro.Output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("expected", doc.RootElement.GetProperty("error").GetProperty("kind").GetString());
        Assert.Equal(
            "O arquivo 'a.ypf' é inválido: truncado",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Erro_de_uso_com_json_sai_como_json()
    {
        var result = await _cli.RunAsync(["--nao-existe", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("--nao-existe", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Opcao_obrigatoria_ausente_sai_em_portugues()
    {
        AddCommandWithParameters();

        var result = await _cli.RunAsync(["importa", "a.po"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A opção '--game' é obrigatória.", result.Error);
        Assert.DoesNotContain("is required", result.Error);
    }

    [Fact]
    public async Task Valor_invalido_sai_em_portugues()
    {
        AddCommandWithParameters();

        var result = await _cli.RunAsync(
            ["importa", "a.po", "--game", "g", "--limit", "abc"],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("O valor 'abc' não é válido para a opção '--limit'", result.Error);
        Assert.DoesNotContain("Cannot parse", result.Error);
    }

    [Fact]
    public async Task Argumento_ausente_e_opcao_sem_valor_saem_em_portugues()
    {
        AddCommandWithParameters();

        var semArgumento = await _cli.RunAsync(["importa", "--game", "g"], TestContext.Current.CancellationToken);
        var semValor = await _cli.RunAsync(["importa", "a.po", "--game"], TestContext.Current.CancellationToken);

        Assert.Contains("argumento obrigatório", semArgumento.Error);
        Assert.Contains("A opção '--game' precisa de um valor.", semValor.Error);
    }

    [Fact]
    public async Task Comando_desconhecido_sai_em_portugues_e_com_json()
    {
        var result = await _cli.RunAsync(["nada", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("'nada' não é um comando", message);
    }

    [Fact]
    public async Task Project_sem_valor_engole_a_opcao_seguinte_como_pasta()
    {
        var result = await _cli.RunAsync(["--project", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--json", result.Error);
    }

    private void AddCommandWithParameters()
    {
        var command = new System.CommandLine.Command("importa", "Comando de teste.");
        command.Arguments.Add(new System.CommandLine.Argument<string>("arquivo"));
        command.Options.Add(new System.CommandLine.Option<string>("--game") { Required = true });
        command.Options.Add(new System.CommandLine.Option<int>("--limit"));
        command.SetAction(_ => 0);
        _cli.Cli.Root.Subcommands.Add(command);
    }
}
