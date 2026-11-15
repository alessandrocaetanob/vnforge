using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Cost;

/// <summary>Os preços da página de preços da API em 09/11/2026 (platform.claude.com), com a faixa de prompt longo do Haiku 5.5.</summary>
[Trait("Risco", "R5")]
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
}
