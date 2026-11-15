using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core;
using VNForge.Core.Text;
using VNForge.Patching;

namespace VNForge.AI.Tests.Cost;

[Trait("Risco", "R2")]
[Trait("Risco", "R5")]
[Trait("Risco", "R7")]
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

    // Disco que não é local e zera o destino depois do rename (rclone com cache em modo full, #697): o Save tem que recusar e deixar o cache anterior como estava.
    private sealed class ZeroingDisk : IProjectDisk
    {
        public bool IsLocal(string path) => false;

        public void Move(string source, string destination, bool overwrite)
        {
            File.Move(source, destination, overwrite);
            File.WriteAllBytes(destination, []);
        }
    }

    [Fact]
    public void R2_cache_gravado_num_disco_que_zera_o_rename_recusa_e_mantem_o_anterior()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"vnf-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "translations.json");
        try
        {
            var cache = new TranslationCache();
            cache.Set("a", "paga");
            cache.Save(path);
            var before = File.ReadAllBytes(path);
            cache.Set("b", "nova");

            Exception? error;
            using (ProjectDisk.Use(new ZeroingDisk()))
            {
                error = Record.Exception(() => cache.Save(path));
            }

            Assert.IsAssignableFrom<IOException>(error);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void R7_cache_vazio_na_leitura_recusa_em_portugues_e_nao_mexe_no_arquivo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vnf-cache-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(path, []);
        try
        {
            var error = Assert.Throws<InvalidFileException>(() => TranslationCache.Load(path));

            Assert.Contains("apague", error.Message);
            Assert.True(File.Exists(path));
            Assert.Empty(File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Provedor_que_omite_uma_linha_falha_sem_gravar_no_cache()
    {
        var cache = new TranslationCache();
        var provider = new CachingTranslationProvider(new OmittingProvider(), cache, ClaudeModels.Opus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TranslateAsync(new TranslationBatch([Unit("a", "はい")]), CancellationToken.None));
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
    public void Preco_calcula_por_milhao_de_tokens_e_recusa_modelo_desconhecido()
    {
        Assert.Equal(24m, ModelPricing.For(ClaudeModels.Opus).Cost(new TokenUsage(1_000_000, 1_000_000)));
        Assert.Throws<ArgumentException>(() => ModelPricing.For("gpt-x"));
        Assert.All(ClaudeModels.All, m => ModelPricing.For(m));
    }
}
