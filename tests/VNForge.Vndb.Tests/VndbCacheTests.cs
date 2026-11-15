using Microsoft.Extensions.Time.Testing;

namespace VNForge.Vndb.Tests;

[Trait("Risco", "R7")]
public class VndbCacheTests
{
    [Fact]
    public void Chave_ausente_e_arquivo_corrompido_contam_como_falta()
    {
        using var dir = new TempDir();
        var cache = new VndbCache(dir.Path, new FakeTimeProvider());
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "ruim.json"), "{ nao e json");

        Assert.False(cache.TryGet("nao-existe", out _));
        Assert.False(cache.TryGet("ruim", out _));
    }
}
