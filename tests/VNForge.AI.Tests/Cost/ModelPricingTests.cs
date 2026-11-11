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
}
