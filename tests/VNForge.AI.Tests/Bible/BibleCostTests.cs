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
    public void Sem_capitulos_nao_custa_nada()
    {
        var estimate = BibleCost.Estimate([], ClaudeModels.Opus);

        Assert.Equal(0m, estimate.Dollars);
        Assert.Equal(0m, estimate.RereadDollars);
    }
}
