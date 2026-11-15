using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

[Trait("Risco", "R5")]
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
}
