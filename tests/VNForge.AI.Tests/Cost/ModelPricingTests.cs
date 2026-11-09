using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Cost;

/// <summary>Os preços da página de preços da API em 09/11/2026 (platform.claude.com), com a faixa de prompt longo do Haiku 5.5.</summary>
public sealed class ModelPricingTests
{
    [Theory]
    [InlineData("claude-fable-5-1", 10.00, 50.00, 0.25, 12.50)]
    [InlineData("claude-opus-5-5", 4.00, 20.00, 0.20, 5.00)]
    [InlineData("claude-sonnet-5-5", 2.00, 10.00, 0.10, 2.50)]
    [InlineData("claude-haiku-5-5", 0.10, 0.50, 0.01, 0.125)]
    public void Tabela_confere_com_a_pagina_de_precos(string model, double input, double output, double cacheRead, double cacheWrite)
    {
        Assert.Equal(new ModelPrice((decimal)input, (decimal)output, (decimal)cacheRead, (decimal)cacheWrite), ModelPricing.For(model));
    }

    [Fact]
    public void Haiku_acima_de_100_mil_tokens_de_prompt_paga_a_faixa_longa()
    {
        var longTier = new ModelPrice(0.50m, 2.50m, 0.05m, 0.625m);

        Assert.Equal(ModelPricing.For(ClaudeModels.Haiku), ModelPricing.For(ClaudeModels.Haiku, 100_000));
        Assert.Equal(longTier, ModelPricing.For(ClaudeModels.Haiku, 100_001));
    }

    [Fact]
    public void Modelo_sem_faixa_longa_cobra_o_mesmo_preco_em_qualquer_tamanho()
    {
        Assert.All(
            new[] { ClaudeModels.Opus, ClaudeModels.Sonnet, ClaudeModels.Fable },
            model => Assert.Equal(ModelPricing.For(model), ModelPricing.For(model, 700_000)));
    }

    [Fact]
    public void Uso_acumulado_cobra_a_parte_de_pedidos_longos_na_faixa_longa()
    {
        var shortRequest = ClaudeUsage.ForRequest(50_000, 1_000, 0, 0);
        var longRequest = ClaudeUsage.ForRequest(500, 2_000, 200_000, 0);

        var dollars = RunCostTracker.DollarsFor(ClaudeModels.Haiku, shortRequest + longRequest);

        var expected = ModelPricing.For(ClaudeModels.Haiku).Cost(new TokenUsage(50_000, 1_000))
            + new ModelPrice(0.50m, 2.50m, 0.05m, 0.625m).Cost(new TokenUsage(500, 2_000, 200_000));
        Assert.Equal(expected, dollars);
        Assert.Equal(new ClaudeUsage(500, 2_000, 200_000, 0), (shortRequest + longRequest).InLongPrompts);
    }

    [Fact]
    public void Pedido_ate_o_limite_nao_marca_prompt_longo()
    {
        Assert.Null(ClaudeUsage.ForRequest(40_000, 10, 60_000, 0).InLongPrompts);
        Assert.NotNull(ClaudeUsage.ForRequest(40_000, 10, 60_000, 1).InLongPrompts);
    }

    [Fact]
    public void Estimativa_da_biblia_com_capitulo_acima_de_100_mil_tokens_usa_a_faixa_longa_do_Haiku()
    {
        var chapter = Chapter("Longo", 1, Scene("yst1", "a", new string('あ', 1_000_000)), Scene("yst2", "b", "ただいま"));

        var estimate = BibleCost.Estimate([chapter], ClaudeModels.Haiku, scenesPerCall: 1, includeOverview: false);

        Assert.True(estimate.Usage.CacheWrite > ModelPricing.LongPromptTokens);
        Assert.Equal(ModelPricing.For(ClaudeModels.Haiku, ModelPricing.LongPromptTokens + 1).Cost(estimate.Usage), estimate.Dollars);
        Assert.True(estimate.Dollars > ModelPricing.For(ClaudeModels.Haiku).Cost(estimate.Usage));
    }

    [Fact]
    public void Estimativa_da_biblia_de_capitulo_curto_segue_a_faixa_curta()
    {
        var chapter = Chapter("Curto", 1, Scene("yst1", "a", "おはよう"), Scene("yst2", "b", "ただいま"));

        var estimate = BibleCost.Estimate([chapter], ClaudeModels.Haiku, scenesPerCall: 1);

        Assert.Equal(ModelPricing.For(ClaudeModels.Haiku).Cost(estimate.Usage), estimate.Dollars);
    }
}
