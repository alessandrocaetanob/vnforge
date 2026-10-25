using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

public class ExchangeRateTests
{
    private static TextUnit Unit(int n) =>
        new($"u{n}", TextUnitKind.Dialogue, null, "こんにちは、今日はいい天気ですね。", null, TranslationStatus.Untranslated);

    [Fact]
    public void Converte_dolar_em_real_com_a_cotacao_informada()
    {
        var rate = new ExchangeRate(5.40m, new DateOnly(2026, 10, 25));

        Assert.Equal(54.00m, rate.ToBrl(10m));
        Assert.True(rate.IsInformed);
        Assert.Equal(new DateOnly(2026, 10, 25), rate.InformedOn);
    }

    [Fact]
    public void Cotacao_sem_data_vale_como_nao_informada()
    {
        Assert.False(new ExchangeRate(5.40m).IsInformed);
        Assert.False(ExchangeRate.Placeholder.IsInformed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Recusa_cotacao_zero_ou_negativa(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExchangeRate(value));
    }

    [Fact]
    public void Quote_devolve_dolar_e_real_da_mesma_estimativa()
    {
        var lines = Enumerable.Range(0, 100).Select(Unit).ToList();
        var estimate = CostEstimator.Estimate(lines, ClaudeModels.Sonnet);
        var rate = new ExchangeRate(5m, new DateOnly(2026, 10, 25));

        var quote = CostEstimator.Quote(estimate, rate);

        Assert.Equal(estimate.Dollars, quote.Dollars);
        Assert.Equal(estimate.Dollars * 5m, quote.Brl);
        Assert.Equal(quote.Brl, CostEstimator.Brl(estimate, rate));
    }

    [Fact]
    public void Quote_das_linhas_usa_o_preco_do_modelo_e_a_cotacao()
    {
        var lines = Enumerable.Range(0, 50).Select(Unit).ToList();
        var rate = new ExchangeRate(6m, new DateOnly(2026, 10, 25));

        var opus = CostEstimator.Quote(lines, ClaudeModels.Opus, rate);
        var haiku = CostEstimator.Quote(lines, ClaudeModels.Haiku, rate);

        Assert.Equal(CostEstimator.Estimate(lines, ClaudeModels.Opus).Dollars * 6m, opus.Brl);
        Assert.True(haiku.Brl < opus.Brl);
        Assert.Equal(50, opus.Estimate.Lines);
    }

    [Fact]
    public void QuoteForModels_cobre_cada_modelo_na_ordem_pedida_com_a_mesma_cotacao()
    {
        var lines = Enumerable.Range(0, 20).Select(Unit).ToList();
        var rate = new ExchangeRate(5.5m, new DateOnly(2026, 10, 25));

        var quotes = CostEstimator.QuoteForModels(lines, [ClaudeModels.Haiku, ClaudeModels.Opus], rate);

        Assert.Equal([ClaudeModels.Haiku, ClaudeModels.Opus], quotes.Keys);
        Assert.Equal(CostEstimator.Estimate(lines, ClaudeModels.Opus).Dollars * 5.5m, quotes[ClaudeModels.Opus].Brl);
    }
}
