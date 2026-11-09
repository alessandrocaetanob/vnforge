using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

public sealed class BibleCostTests
{
    private static BibleChapterInput Big(string name, int scenes, int linesPerScene = 200) =>
        Chapter(name, 1, [.. Enumerable.Range(0, scenes).Select(i => Scene($"yst{i}", $"c{i}", [.. Enumerable.Repeat(new string('あ', 40), linesPerScene)]))]);

    [Fact]
    public void A_primeira_chamada_do_capitulo_escreve_o_cache_e_as_outras_o_leem()
    {
        var chapter = Big("Manhã", scenes: 30);

        var estimate = BibleCost.Estimate([chapter], ClaudeModels.Opus, scenesPerCall: 12, includeOverview: false);

        // 30 cenas em chamadas de 12: 3 chamadas, 1 escrita e 2 leituras do mesmo roteiro (o texto, a instrução e a lista de personagens).
        Assert.Equal(chapter.Tokens() + 458, estimate.Usage.CacheWrite);
        Assert.Equal(estimate.Usage.CacheWrite * 2, estimate.Usage.CacheRead);
        Assert.Equal(1, estimate.Chapters);
        Assert.Equal(30, estimate.Scenes);
        Assert.Equal(6000, estimate.Lines);
    }

    [Fact]
    public void Reescrever_um_campo_e_uma_chamada_de_uma_cena_com_a_escrita_do_cache_do_capitulo()
    {
        var chapter = Big("Manhã", scenes: 30);

        var rewrite = BibleCost.EstimateRewrite(chapter, ClaudeModels.Opus);
        var reading = BibleCost.Estimate([chapter], ClaudeModels.Opus, scenesPerCall: 12, includeOverview: false);

        Assert.Equal(chapter.Tokens() + 458, rewrite.Usage.CacheWrite);
        Assert.Equal(0, rewrite.Usage.CacheRead);
        Assert.Equal(330 + 1200, rewrite.Usage.Output);
        Assert.Equal(1, rewrite.Scenes);
        Assert.True(rewrite.Dollars < reading.Dollars);
        Assert.Equal(ModelPricing.For(ClaudeModels.Opus).Cost(rewrite.Usage), rewrite.Dollars);
    }

    [Fact]
    public void Ler_de_novo_custa_menos_que_a_primeira_vez_por_causa_do_cache()
    {
        var chapter = Big("Manhã", scenes: 60);
        var withBatches = BibleCost.Estimate([chapter], ClaudeModels.Opus, scenesPerCall: 12, includeOverview: false);
        var oneCall = BibleCost.Estimate([chapter], ClaudeModels.Opus, scenesPerCall: 60, includeOverview: false);

        // Cinco chamadas pagariam a entrada cheia (4,00 por milhão) cinco vezes; com o cache, uma escrita (5,00) e quatro leituras (0,20).
        var text = oneCall.Usage.CacheWrite;
        var uncachedInput = ModelPricing.For(ClaudeModels.Opus).Input * 5 * text / 1_000_000m;
        var cachedInput = ModelPricing.For(ClaudeModels.Opus).Cost(new TokenUsage(0, 0, withBatches.Usage.CacheRead, withBatches.Usage.CacheWrite));
        Assert.True(cachedInput < uncachedInput / 2);
        Assert.Equal(4 * text, withBatches.Usage.CacheRead);
        Assert.True(withBatches.Usage.CacheRead > 0);
        Assert.Equal(0, oneCall.Usage.CacheRead);
    }

    [Fact]
    public void A_sinopse_soma_uma_chamada_pequena()
    {
        var chapter = Big("Manhã", scenes: 12);

        var without = BibleCost.Estimate([chapter], ClaudeModels.Opus, includeOverview: false);
        var with = BibleCost.Estimate([chapter], ClaudeModels.Opus, includeOverview: true);

        Assert.True(with.Dollars > without.Dollars);
        Assert.True(with.Dollars - without.Dollars < without.Dollars / 5);
    }

    [Fact]
    public void O_preco_segue_o_modelo_e_a_cotacao_converte_para_reais()
    {
        // Abaixo de 100 mil tokens de prompt: acima disso o Haiku cobra a faixa longa e a diferença para o Opus encolhe.
        var chapters = new[] { Big("Manhã", scenes: 6) };

        var opus = BibleCost.Estimate(chapters, ClaudeModels.Opus);
        var haiku = BibleCost.Estimate(chapters, ClaudeModels.Haiku);

        Assert.True(opus.Dollars > haiku.Dollars * 10);
        Assert.Equal(opus.Dollars * 5.40m, opus.Brl(new ExchangeRate(5.40m)));
        Assert.Equal(opus.RereadDollars * 5.40m, opus.RereadBrl(new ExchangeRate(5.40m)));
    }

    [Fact]
    public void Reler_um_capitulo_e_a_media_dos_capitulos_sem_a_sinopse()
    {
        var a = Big("A", scenes: 12);
        var b = Big("B", scenes: 36, linesPerScene: 300);

        var both = BibleCost.Estimate([a, b], ClaudeModels.Opus);
        var onlyA = BibleCost.Estimate([a], ClaudeModels.Opus, includeOverview: false);
        var onlyB = BibleCost.Estimate([b], ClaudeModels.Opus, includeOverview: false);

        Assert.Equal((onlyA.Dollars + onlyB.Dollars) / 2, both.RereadDollars);
    }

    [Fact]
    public void Sem_capitulos_nao_custa_nada()
    {
        var estimate = BibleCost.Estimate([], ClaudeModels.Opus);

        Assert.Equal(0m, estimate.Dollars);
        Assert.Equal(0m, estimate.RereadDollars);
    }
}
