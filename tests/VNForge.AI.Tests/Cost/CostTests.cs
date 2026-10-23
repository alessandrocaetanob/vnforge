using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

public class CostTests
{
    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    [Fact]
    public async Task Segunda_execucao_da_mesma_traducao_nao_chama_o_provedor()
    {
        var fake = new FakeTranslationProvider();
        var cache = new TranslationCache();
        var ledger = new UsageLedger();
        var provider = new CachingTranslationProvider(fake, cache, ClaudeModels.Opus, ledger);
        var batch = new TranslationBatch([Unit("a", "こんにちは"), Unit("b", "さようなら")]);

        var first = await provider.TranslateAsync(batch, CancellationToken.None);
        var second = await provider.TranslateAsync(batch, CancellationToken.None);

        Assert.Single(fake.ReceivedBatches);
        Assert.Equal(first.Select(r => r.Translation), second.Select(r => r.Translation));
        Assert.Equal(2, ledger.CacheHits);
    }

    [Fact]
    public async Task So_as_linhas_novas_vao_ao_provedor_e_a_ordem_do_lote_e_mantida()
    {
        var fake = new FakeTranslationProvider();
        var provider = new CachingTranslationProvider(fake, new TranslationCache(), ClaudeModels.Opus);
        await provider.TranslateAsync(new TranslationBatch([Unit("a", "あ")]), CancellationToken.None);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("b", "い"), Unit("a", "あ"), Unit("c", "う")]), CancellationToken.None);

        Assert.Equal(["b", "a", "c"], results.Select(r => r.Id));
        Assert.Equal(["b", "c"], fake.ReceivedBatches.Last().Lines.Select(l => l.Id));
    }

    [Fact]
    public async Task Linhas_iguais_no_mesmo_lote_pagam_uma_vez()
    {
        var fake = new FakeTranslationProvider();
        var provider = new CachingTranslationProvider(fake, new TranslationCache(), ClaudeModels.Opus);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "はい"), Unit("b", "はい")]), CancellationToken.None);

        Assert.Single(fake.ReceivedBatches.Single().Lines);
        Assert.Equal(results[0].Translation, results[1].Translation);
    }

    [Fact]
    public async Task Mudar_o_modelo_ou_o_estilo_invalida_o_cache()
    {
        var fake = new FakeTranslationProvider();
        var cache = new TranslationCache();
        var lines = new[] { Unit("a", "はい") };
        await new CachingTranslationProvider(fake, cache, ClaudeModels.Opus).TranslateAsync(new TranslationBatch(lines), CancellationToken.None);

        await new CachingTranslationProvider(fake, cache, ClaudeModels.Haiku).TranslateAsync(new TranslationBatch(lines), CancellationToken.None);
        await new CachingTranslationProvider(fake, cache, ClaudeModels.Opus).TranslateAsync(new TranslationBatch(lines) { StyleGuide = "formal" }, CancellationToken.None);

        Assert.Equal(3, fake.ReceivedBatches.Count);
    }

    [Fact]
    public async Task Cache_salvo_em_disco_vale_na_proxima_execucao()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnf-cache-{Guid.NewGuid():N}.json");
        try
        {
            var fake = new FakeTranslationProvider();
            var cache = new TranslationCache();
            var batch = new TranslationBatch([Unit("a", "はい")]);
            await new CachingTranslationProvider(fake, cache, ClaudeModels.Opus).TranslateAsync(batch, CancellationToken.None);
            cache.Save(path);

            var second = new FakeTranslationProvider();
            await new CachingTranslationProvider(second, TranslationCache.Load(path), ClaudeModels.Opus).TranslateAsync(batch, CancellationToken.None);

            Assert.Empty(second.ReceivedBatches);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_corrompido_vira_erro_claro()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnf-cache-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ nao e json");
        try
        {
            Assert.Throws<InvalidDataException>(() => TranslationCache.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Estimativa_ignora_linhas_em_cache_e_calcula_o_custo()
    {
        var cache = new TranslationCache();
        var cached = Unit("a", "はい");
        cache.Set(TranslationCache.KeyFor(cached, ""), "sim");

        var estimate = CostEstimator.Estimate([cached, Unit("b", "こんにちは")], ClaudeModels.Opus, cache);

        Assert.Equal(1, estimate.Lines);
        Assert.Equal(1, estimate.CachedLines);
        Assert.True(estimate.Usage.Input > 0 && estimate.Usage.Output > 0);
        Assert.Equal(ModelPricing.For(ClaudeModels.Opus).Cost(estimate.Usage), estimate.Dollars);
    }

    [Fact]
    public async Task Estimativa_por_lotes_reconhece_o_cache_gravado_pelo_provedor()
    {
        var cache = new TranslationCache();
        var batch = new TranslationBatch([Unit("a", "はい")]) { StyleGuide = "informal" };
        await new CachingTranslationProvider(new FakeTranslationProvider(), cache, ClaudeModels.Opus).TranslateAsync(batch, CancellationToken.None);

        var estimate = CostEstimator.EstimateBatches([batch], ClaudeModels.Opus, cache);

        Assert.Equal(0, estimate.Lines);
        Assert.Equal(1, estimate.CachedLines);
    }

    [Fact]
    public async Task Provedor_que_omite_uma_linha_falha_sem_gravar_no_cache()
    {
        var cache = new TranslationCache();
        var provider = new CachingTranslationProvider(new OmittingProvider(), cache, ClaudeModels.Opus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateAsync(new TranslationBatch([Unit("a", "はい")]), CancellationToken.None));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task Linha_recusada_nao_vai_para_o_cache_e_continua_marcada_como_recusada()
    {
        var cache = new TranslationCache();
        var provider = new CachingTranslationProvider(new RefusingProvider(), cache, ClaudeModels.Opus);

        var results = await provider.TranslateAsync(new TranslationBatch([Unit("a", "はい")]), CancellationToken.None);

        Assert.True(results[0].Refused);
        Assert.Equal(string.Empty, results[0].Translation);
        Assert.Equal(0, cache.Count);
    }

    private sealed class RefusingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>([.. batch.Lines.Select(l => new TranslationResult(l.Id, string.Empty, Refused: true))]);
    }

    private sealed class OmittingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>([]);
    }

    [Fact]
    public void Estimativa_sem_linhas_pendentes_custa_zero()
    {
        var estimate = CostEstimator.Estimate([], ClaudeModels.Opus);

        Assert.Equal(0m, estimate.Dollars);
    }

    [Fact]
    public void Preco_calcula_por_milhao_de_tokens_e_recusa_modelo_desconhecido()
    {
        Assert.Equal(24m, ModelPricing.For(ClaudeModels.Opus).Cost(new TokenUsage(1_000_000, 1_000_000)));
        Assert.Throws<ArgumentException>(() => ModelPricing.For("gpt-x"));
        Assert.All(ClaudeModels.All, m => ModelPricing.For(m));
    }

    [Fact]
    public async Task Contador_soma_chamadas_em_paralelo()
    {
        var ledger = new UsageLedger();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => ledger.Record(new TokenUsage(10, 5, 1, 2)))));

        Assert.Equal(50, ledger.Calls);
        Assert.Equal(new TokenUsage(500, 250, 50, 100), ledger.Total);
    }
}
