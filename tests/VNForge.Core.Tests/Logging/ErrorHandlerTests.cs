using VNForge.Core.Logging;

namespace VNForge.Core.Tests.Logging;

public sealed class ErrorHandlerTests : IDisposable
{
    private const string ErrorLiteral = "error";
    private const string TestMessage = "Teste";
    private const string BrokenState = "estado quebrado";
    private const string AYpf = "a.ypf";

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
            factory.CreateLogger(TestMessage),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: cancellationToken);
    }

    private static CancellationToken Cancelled() => new(canceled: true);

    [Fact]
    public void Versao_desconhecida_tem_mensagem_em_portugues()
    {
        var ex = new UnknownVersionException("ysbin.ypf", "999");

        Assert.Equal("Versão desconhecida (999) no arquivo 'ysbin.ypf'.", ex.Message);
        Assert.IsType<VNForgeException>(ex, exactMatch: false);
    }

    [Fact]
    public void Excecao_inesperada_mostra_so_o_aviso_e_manda_o_stack_completo_para_o_log()
    {
        var error = new StringWriter();

        var code = Run(() => throw new InvalidOperationException(BrokenState), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.Contains("Erro inesperado", text);
        Assert.Contains(VNForgeLogging.GetLogDirectory(_root), text);
        Assert.DoesNotContain(BrokenState, text);
        Assert.DoesNotContain(" at ", text);

        var log = ReadLog();
        Assert.Contains("System.InvalidOperationException: estado quebrado", log);
        Assert.Contains("ErrorHandlerTests", log);
    }

    [Fact]
    public void Erro_inesperado_com_logger_que_lanca_mostra_o_erro_original_no_console()
    {
        var error = new StringWriter();

        var code = ErrorHandler.Execute(
            () => throw new InvalidOperationException(BrokenState),
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
            factory.CreateLogger(TestMessage),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: cts.Token);

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
            action, factory.CreateLogger(TestMessage), error, VNForgeLogging.GetLogDirectory(_root), json, cancellationToken);
        return (code, json.ToString(), error.ToString());
    }

    [Fact]
    public void Json_de_excecao_do_Core_tem_formato_comum_e_nao_escreve_no_error()
    {
        var (code, json, error) = RunJson(() => throw new InvalidFileException(AYpf, "truncado"));

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.Equal(string.Empty, error);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("expected", root.GetProperty(ErrorLiteral).GetProperty("kind").GetString());
        Assert.Equal("O arquivo 'a.ypf' é inválido: truncado", root.GetProperty(ErrorLiteral).GetProperty("message").GetString());
        Assert.DoesNotContain(" at ", json);
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
