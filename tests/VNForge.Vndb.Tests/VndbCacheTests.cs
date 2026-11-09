using Microsoft.Extensions.Time.Testing;

namespace VNForge.Vndb.Tests;

public class VndbCacheTests
{
    [Fact]
    public void Chave_depende_do_caminho_e_do_corpo()
    {
        var key = VndbCache.KeyFor("vn", "{}");

        Assert.Equal(key, VndbCache.KeyFor("vn", "{}"));
        Assert.NotEqual(key, VndbCache.KeyFor("character", "{}"));
        Assert.NotEqual(key, VndbCache.KeyFor("vn", """{"a":1}"""));
        Assert.Equal(64, key.Length);
    }

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
    public void Entrada_com_data_no_futuro_conta_como_vencida_e_e_apagada()
    {
        using var dir = new TempDir();
        var later = new FakeTimeProvider();
        later.Advance(TimeSpan.FromDays(1));
        new VndbCache(dir.Path, later).Set("k", "resposta");

        // Outro relógio, um dia atrás (data do sistema ajustada para trás): a entrada gravada passa a ser "do futuro".
        var cache = new VndbCache(dir.Path, new FakeTimeProvider());

        Assert.False(cache.TryGet("k", out _));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void Entrada_vencida_e_apagada_na_leitura()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var cache = new VndbCache(dir.Path, time, TimeSpan.FromHours(1));
        cache.Set("k", "resposta");
        cache.Set("outra", "resposta");

        time.Advance(TimeSpan.FromHours(1));

        Assert.False(cache.TryGet("k", out _));
        Assert.Equal(["outra.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
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
    public void Gravar_de_novo_troca_a_resposta_e_nao_deixa_arquivo_temporario()
    {
        using var dir = new TempDir();
        var cache = new VndbCache(dir.Path, new FakeTimeProvider());

        cache.Set("k", "um");
        cache.Set("k", "dois");

        Assert.True(cache.TryGet("k", out var hit));
        Assert.Equal("dois", hit);
        Assert.Equal(["k.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
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
