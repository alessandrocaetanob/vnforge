using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Dictionaries;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

/// <summary>O pré-dicionário no caminho da IA: o lote leva o original normalizado e a chave do cache lê o que a IA lê (#539).</summary>
public class PreDictionaryTests
{
    private static readonly IReadOnlyList<GlossaryTerm> NoTerms = [];

    private static readonly CharacterBook NoCast = new([]);

    private static TextUnit Unit(string id, string source, string? speaker = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static TranslationBatchBuilder Builder(params DictionaryRule[] pre) =>
        new(NoTerms, NoCast) { Pre = new PreDictionary(pre) };

    private static string Key(TranslationBatchBuilder builder, TextUnit unit)
    {
        var batch = builder.Build([unit]);
        return TranslationCache.KeyFor(batch.Lines[0], TranslationCache.ContextFor("claude-opus-5-5", batch));
    }

    [Fact]
    public void Lote_leva_o_original_normalizado_e_a_traducao_volta_pelo_id_da_linha()
    {
        var batch = Builder(new DictionaryRule("ぼ、ぼく", "ぼく")).Build([Unit("a:1", "ぼ、ぼく、行く")]);

        Assert.Equal("ぼく、行く", batch.Lines[0].Source);
        Assert.Equal("a:1", batch.Lines[0].Id);
    }

    [Fact]
    public void Contexto_de_antes_e_de_depois_tambem_vai_normalizado()
    {
        var builder = Builder(new DictionaryRule("ぼ、ぼく", "ぼく"));

        var batch = builder.Build([Unit("a:2", "はい")], previous: [Unit("a:1", "ぼ、ぼく") with { Translation = "Eu", Status = TranslationStatus.Translated }], next: [Unit("a:3", "ぼ、ぼく")]);

        Assert.Equal("ぼく", batch.PreviousLines[0].Source);
        Assert.Equal("ぼく", batch.NextLines[0].Source);
    }

    [Fact]
    public void Glossario_casa_com_o_texto_normalizado()
    {
        var builder = new TranslationBatchBuilder([new GlossaryTerm("先輩", "senpai")], NoCast) { Pre = new PreDictionary([new DictionaryRule("せんぱい", "先輩")]) };

        var batch = builder.Build([Unit("a:1", "せんぱい、こんにちは")]);

        Assert.Equal("先輩", Assert.Single(batch.Glossary).Source);
    }

    [Fact]
    public void Chave_do_cache_muda_quando_uma_regra_do_pre_muda_o_original_da_linha()
    {
        var line = Unit("a:1", "ぼ、ぼく、行く");

        var without = Key(Builder(), line);
        var withRule = Key(Builder(new DictionaryRule("ぼ、ぼく", "ぼく")), line);
        var otherRule = Key(Builder(new DictionaryRule("ぼ、ぼく", "僕")), line);

        Assert.NotEqual(without, withRule);
        Assert.NotEqual(withRule, otherRule);
        Assert.Equal(withRule, Key(Builder(new DictionaryRule("ぼ、ぼく", "ぼく")), line));
    }

    [Fact]
    public void Chave_do_cache_leva_a_janela_de_contexto_e_o_original_normalizado()
    {
        var rule = new DictionaryRule("ぼ、ぼく", "ぼく");
        var line = Unit("a:1", "ぼ、ぼく、行く");
        string KeyWith(int before, params DictionaryRule[] pre) =>
            Key(new TranslationBatchBuilder(NoTerms, NoCast, maxPreviousLines: before) { Pre = new PreDictionary(pre) }, line);

        Assert.NotEqual(KeyWith(2, rule), KeyWith(4, rule));
        Assert.NotEqual(KeyWith(4), KeyWith(4, rule));
        Assert.Equal(KeyWith(4, rule), KeyWith(4, rule));
    }

    [Fact]
    public void Chave_do_cache_das_linhas_que_a_regra_nao_toca_continua_a_mesma()
    {
        var untouched = Unit("a:1", "おはよう");

        Assert.Equal(Key(Builder(), untouched), Key(Builder(new DictionaryRule("ぼ、ぼく", "ぼく")), untouched));
    }

    [Fact]
    public void Regra_condicionada_ao_falante_so_muda_a_chave_da_fala_dele()
    {
        var rule = new DictionaryRule("せやな", "そうだな") { Conditions = [new(DictionaryConditionKind.Speaker, "蓮")] };

        Assert.NotEqual(Key(Builder(), Unit("a:1", "せやな", "蓮")), Key(Builder(rule), Unit("a:1", "せやな", "蓮")));
        Assert.Equal(Key(Builder(), Unit("a:2", "せやな", "葵")), Key(Builder(rule), Unit("a:2", "せやな", "葵")));
    }

    [Fact]
    public async Task Traducao_em_cache_antes_da_regra_nao_serve_depois_dela()
    {
        var cache = new TranslationCache();
        var line = Unit("a:1", "ぼ、ぼく、行く");
        var provider = new FakeTranslationProvider();
        var old = Builder();
        var recent = Builder(new DictionaryRule("ぼ、ぼく", "ぼく"));

        await new CachingTranslationProvider(provider, cache, "claude-opus-5-5").TranslateAsync(old.Build([line]), TestContext.Current.CancellationToken);
        await new CachingTranslationProvider(provider, cache, "claude-opus-5-5").TranslateAsync(recent.Build([line]), TestContext.Current.CancellationToken);

        var received = provider.ReceivedBatches.ToList();
        Assert.Equal(2, received.Count);
        Assert.Equal("ぼ、ぼく、行く", received[0].Lines[0].Source);
        Assert.Equal("ぼく、行く", received[1].Lines[0].Source);
    }
}
