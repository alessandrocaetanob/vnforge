using Microsoft.Extensions.Logging;
using VNForge.Core.Logging;

namespace VNForge.Core.Tests.Logging;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void GetLogDirectory_aponta_para_cache_logs_do_projeto()
    {
        var expected = Path.Combine(_root, ".cache", "logs");

        Assert.Equal(expected, VNForgeLogging.GetLogDirectory(_root));
    }

    [Fact]
    public void Log_completo_vai_para_cache_logs_do_projeto()
    {
        using (var factory = VNForgeLogging.CreateFactory(_root))
        {
            var logger = factory.CreateLogger("Teste");
            logger.LogInformation("Lendo {Arquivo}", "ysbin.ypf");
            logger.LogError(new InvalidOperationException("falha interna"), "Falhou");
        }

        var files = Directory.GetFiles(VNForgeLogging.GetLogDirectory(_root), "*.log");
        var file = Assert.Single(files);
        var content = File.ReadAllText(file);
        Assert.Contains("[Information] Teste: Lendo ysbin.ypf", content);
        Assert.Contains("[Error] Teste: Falhou", content);
        Assert.Contains("System.InvalidOperationException: falha interna", content);
    }

    [Fact]
    public void Nivel_minimo_filtra_mensagens_abaixo_dele()
    {
        using (var factory = VNForgeLogging.CreateFactory(_root, LogLevel.Warning))
        {
            var logger = factory.CreateLogger("Teste");
            logger.LogInformation("ignorada");
            logger.LogWarning("registrada");
        }

        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(VNForgeLogging.GetLogDirectory(_root))));
        Assert.DoesNotContain("ignorada", content);
        Assert.Contains("registrada", content);
    }

    [Fact]
    public void Falha_ao_gravar_o_log_nao_derruba_o_programa()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, ".cache"), "arquivo no lugar da pasta");

        using var factory = VNForgeLogging.CreateFactory(_root);
        var logger = factory.CreateLogger("Teste");

        var erro = Record.Exception(() => logger.LogError("sem onde gravar"));

        Assert.Null(erro);
    }
}
