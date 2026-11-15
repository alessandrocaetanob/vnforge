using Microsoft.Extensions.Logging;
using VNForge.Core.Logging;

namespace VNForge.Core.Tests.Logging;

[Trait("Risco", "R7")]
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
