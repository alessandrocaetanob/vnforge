using VNForge.Core.Logging;

namespace VNForge.Core.Tests.Logging;

public sealed class ErrorHandlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ReadLog()
    {
        var file = Assert.Single(Directory.GetFiles(VNForgeLogging.GetLogDirectory(_root), "*.log"));
        return File.ReadAllText(file);
    }

    private int Run(Func<int> action, StringWriter error) => RunWith(action, error, TestContext.Current.CancellationToken);

    private int RunWith(Func<int> action, StringWriter error, CancellationToken cancellationToken)
    {
        using var factory = VNForgeLogging.CreateFactory(_root);
        return ErrorHandler.Execute(
            action,
            factory.CreateLogger("Teste"),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: cancellationToken);
    }

    private static CancellationToken Cancelled() => new(canceled: true);

    [Fact]
    public void Excecao_do_Core_vira_mensagem_curta_em_portugues_sem_stack_trace()
    {
        var error = new StringWriter();

        var code = Run(() => throw new InvalidFileException("ysbin.ypf", "assinatura YPF ausente"), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.Equal("Erro: O arquivo 'ysbin.ypf' é inválido: assinatura YPF ausente" + Environment.NewLine, text);
        Assert.DoesNotContain(" at ", text);
        Assert.DoesNotContain("Exception", text);
    }

    [Fact]
    public void Versao_desconhecida_tem_mensagem_em_portugues()
    {
        var ex = new UnknownVersionException("ysbin.ypf", "999");

        Assert.Equal("Versão desconhecida (999) no arquivo 'ysbin.ypf'.", ex.Message);
        Assert.IsAssignableFrom<VNForgeException>(ex);
    }

    [Fact]
    public void Excecao_inesperada_mostra_so_o_aviso_e_manda_o_stack_completo_para_o_log()
    {
        var error = new StringWriter();

        var code = Run(() => throw new InvalidOperationException("estado quebrado"), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.Contains("Erro inesperado", text);
        Assert.Contains(VNForgeLogging.GetLogDirectory(_root), text);
        Assert.DoesNotContain("estado quebrado", text);
        Assert.DoesNotContain(" at ", text);

        var log = ReadLog();
        Assert.Contains("System.InvalidOperationException: estado quebrado", log);
        Assert.Contains("ErrorHandlerTests", log);
    }

    [Fact]
    public void Excecao_do_Core_tambem_fica_no_log()
    {
        Run(() => throw new InvalidFileException("a.ypf", "truncado"), new StringWriter());

        Assert.Contains("a.ypf", ReadLog());
    }

    [Fact]
    public void Sem_excecao_devolve_o_codigo_da_acao_e_nao_escreve_erro()
    {
        var error = new StringWriter();

        var code = Run(() => 0, error);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_trata_excecao_do_Core_do_mesmo_jeito()
    {
        var error = new StringWriter();
        using var factory = VNForgeLogging.CreateFactory(_root);

        var code = await ErrorHandler.ExecuteAsync(
            async _ =>
            {
                await Task.Yield();
                throw new UnknownVersionException("x.ybn", "7");
            },
            factory.CreateLogger("Teste"),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            TestContext.Current.CancellationToken);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.Equal("Erro: Versão desconhecida (7) no arquivo 'x.ybn'." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void Log_que_nao_grava_nao_promete_o_arquivo_e_mostra_o_erro_inesperado_no_console()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ".cache"), "arquivo no lugar da pasta");
        var error = new StringWriter();

        var code = Run(() => throw new InvalidOperationException("estado quebrado"), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.DoesNotContain("O log completo está em", text);
        Assert.Contains("InvalidOperationException: estado quebrado", text);
        Assert.Contains(nameof(Log_que_nao_grava_nao_promete_o_arquivo_e_mostra_o_erro_inesperado_no_console), text);
    }

    [Fact]
    public void Log_que_lanca_excecao_nao_impede_a_mensagem_nem_perde_o_erro_original()
    {
        var error = new StringWriter();

        var code = ErrorHandler.Execute(
            () => throw new InvalidFileException("a.ypf", "truncado"),
            new ThrowingLogger(),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.Equal("Erro: O arquivo 'a.ypf' é inválido: truncado" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void Erro_inesperado_com_logger_que_lanca_mostra_o_erro_original_no_console()
    {
        var error = new StringWriter();

        var code = ErrorHandler.Execute(
            () => throw new InvalidOperationException("estado quebrado"),
            new ThrowingLogger(),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.DoesNotContain("O log completo está em", text);
        Assert.Contains("InvalidOperationException: estado quebrado", text);
    }

    [Fact]
    public void Cancelamento_vira_mensagem_curta_com_codigo_3_e_nao_e_erro_no_log()
    {
        var error = new StringWriter();

        var code = RunWith(() => throw new OperationCanceledException(), error, Cancelled());

        Assert.Equal(ErrorHandler.CancelledExitCode, code);
        Assert.Equal(3, ErrorHandler.CancelledExitCode);
        Assert.Equal("Operação cancelada." + Environment.NewLine, error.ToString());
        var log = ReadLog();
        Assert.DoesNotContain("[Error]", log);
    }

    [Fact]
    public async Task ExecuteAsync_repassa_o_token_e_trata_o_cancelamento()
    {
        var error = new StringWriter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var factory = VNForgeLogging.CreateFactory(_root);

        var code = await ErrorHandler.ExecuteAsync(
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            },
            factory.CreateLogger("Teste"),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cts.Token);

        Assert.Equal(ErrorHandler.CancelledExitCode, code);
        Assert.Equal("Operação cancelada." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void Codigos_de_saida_seguem_o_doc_da_CLI()
    {
        Assert.Equal(0, ErrorHandler.OkExitCode);
        Assert.Equal(1, ErrorHandler.ErrorExitCode);
        Assert.Equal(2, ErrorHandler.QaBlockedExitCode);
        Assert.Equal(3, ErrorHandler.CancelledExitCode);
    }

    private (int Code, string Json, string Error) RunJson(Func<int> action) =>
        RunJsonWith(action, TestContext.Current.CancellationToken);

    private (int Code, string Json, string Error) RunJsonWith(Func<int> action, CancellationToken cancellationToken)
    {
        var json = new StringWriter();
        var error = new StringWriter();
        using var factory = VNForgeLogging.CreateFactory(_root);
        var code = ErrorHandler.Execute(
            action, factory.CreateLogger("Teste"), error, VNForgeLogging.GetLogDirectory(_root), json, cancellationToken);
        return (code, json.ToString(), error.ToString());
    }

    [Fact]
    public void Json_de_excecao_do_Core_tem_formato_comum_e_nao_escreve_no_error()
    {
        var (code, json, error) = RunJson(() => throw new InvalidFileException("a.ypf", "truncado"));

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.Equal(string.Empty, error);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("expected", root.GetProperty("error").GetProperty("kind").GetString());
        Assert.Equal("O arquivo 'a.ypf' é inválido: truncado", root.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(" at ", json);
    }

    [Fact]
    public void Json_de_erro_inesperado_nao_vaza_mensagem_nem_stack_e_aponta_o_log()
    {
        var (code, json, _) = RunJson(() => throw new InvalidOperationException("estado quebrado"));

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.DoesNotContain("estado quebrado", json);
        Assert.DoesNotContain(" at ", json);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var err = doc.RootElement.GetProperty("error");
        Assert.Equal("unexpected", err.GetProperty("kind").GetString());
        Assert.Equal(VNForgeLogging.GetLogDirectory(_root), err.GetProperty("logDirectory").GetString());
        Assert.Contains("estado quebrado", ReadLog());
    }

    [Fact]
    public void Json_de_cancelamento_usa_o_codigo_3()
    {
        var (code, json, error) = RunJsonWith(() => throw new OperationCanceledException(), Cancelled());

        Assert.Equal(ErrorHandler.CancelledExitCode, code);
        Assert.Equal(string.Empty, error);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal("cancelled", doc.RootElement.GetProperty("error").GetProperty("kind").GetString());
        Assert.Equal("Operação cancelada.", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void Cancelamento_sem_o_token_cancelado_e_timeout_e_vira_erro_com_codigo_1()
    {
        var error = new StringWriter();

        var code = Run(() => throw new TaskCanceledException("HttpClient.Timeout"), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.StartsWith("Erro inesperado.", error.ToString());
        Assert.DoesNotContain("Operação cancelada", error.ToString());
        Assert.Contains("HttpClient.Timeout", ReadLog());
    }

    [Fact]
    public void Json_de_cancelamento_sem_o_token_cancelado_e_erro_inesperado()
    {
        var (code, json, _) = RunJson(() => throw new OperationCanceledException());

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("unexpected", doc.RootElement.GetProperty("error").GetProperty("kind").GetString());
    }

    private sealed class ThrowingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new AggregateException("provedor quebrado");
    }
}
