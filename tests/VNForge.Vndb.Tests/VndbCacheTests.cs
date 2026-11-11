using Microsoft.Extensions.Time.Testing;

namespace VNForge.Vndb.Tests;

public class VndbCacheTests
{

    [Fact]
    public void Guarda_e_devolve_a_resposta_ate_vencer()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var cache = new VndbCache(dir.Path, time, TimeSpan.FromHours(1));

        cache.Set("k", """{"results":[]}""");

        Assert.True(cache.TryGet("k", out var hit));
        Assert.Equal("""{"results":[]}""", hit);
        time.Advance(TimeSpan.FromMinutes(59));
        Assert.True(cache.TryGet("k", out _));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(cache.TryGet("k", out _));
    }

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

    [Fact]
    public void Pasta_que_nao_da_para_criar_nao_derruba_a_gravacao()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        var blocker = Path.Combine(dir.Path, "arquivo");
        File.WriteAllText(blocker, "x");
        var cache = new VndbCache(Path.Combine(blocker, "dentro"), new FakeTimeProvider());

        cache.Set("k", "um");

        Assert.False(cache.TryGet("k", out _));
    }
}
