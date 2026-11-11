using VNForge.Core.Logging;

namespace VNForge.Core.Tests.Logging;

public sealed class ErrorHandlerTests : IDisposable
{
    private const string ErrorLiteral = "error";
    private const string TestMessage = "Teste";
    private const string BrokenState = "estado quebrado";

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
            factory.CreateLogger(TestMessage),
            error,
            VNForgeLogging.GetLogDirectory(_root),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        Assert.Equal("Erro: Versão desconhecida (7) no arquivo 'x.ybn'." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void Log_que_nao_grava_nao_promete_o_arquivo_e_mostra_o_erro_inesperado_no_console()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ".cache"), "arquivo no lugar da pasta");
        var error = new StringWriter();

        var code = Run(() => throw new InvalidOperationException(BrokenState), error);

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        var text = error.ToString();
        Assert.DoesNotContain("O log completo está em", text);
        Assert.Contains("InvalidOperationException: estado quebrado", text);
        Assert.Contains(nameof(Log_que_nao_grava_nao_promete_o_arquivo_e_mostra_o_erro_inesperado_no_console), text);
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
    public void Json_de_cancelamento_sem_o_token_cancelado_e_erro_inesperado()
    {
        var (code, json, _) = RunJson(() => throw new OperationCanceledException());

        Assert.Equal(ErrorHandler.ErrorExitCode, code);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("unexpected", doc.RootElement.GetProperty(ErrorLiteral).GetProperty("kind").GetString());
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
