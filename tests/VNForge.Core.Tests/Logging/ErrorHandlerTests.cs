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

    private int Run(Func<int> action, StringWriter error)
    {
        using var factory = VNForgeLogging.CreateFactory(_root);
        return ErrorHandler.Execute(action, factory.CreateLogger("Teste"), error, VNForgeLogging.GetLogDirectory(_root));
    }

    [Fact]
    public void Excecao_do_Core_vira_mensagem_curta_em_portugues_sem_stack_trace()
    {
        var error = new StringWriter();

        var code = Run(() => throw new InvalidFileException("ysbin.ypf", "assinatura YPF ausente"), error);

        Assert.Equal(ErrorHandler.ExpectedErrorExitCode, code);
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

        Assert.Equal(ErrorHandler.UnexpectedErrorExitCode, code);
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
            async () =>
            {
                await Task.Yield();
                throw new UnknownVersionException("x.ybn", "7");
            },
            factory.CreateLogger("Teste"),
            error,
            VNForgeLogging.GetLogDirectory(_root));

        Assert.Equal(ErrorHandler.ExpectedErrorExitCode, code);
        Assert.Equal("Erro: Versão desconhecida (7) no arquivo 'x.ybn'." + Environment.NewLine, error.ToString());
    }
}
