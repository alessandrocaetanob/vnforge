using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.Core.Eval;

namespace VNForge.AI.Tests.Eval;

[Trait("Risco", "R5")]
public sealed class EvalConfigParserTests
{
    [Fact]
    public void Recusa_mais_de_26_configuracoes()
    {
        Assert.Throws<EvalException>(() => EvalConfigParser.ParseAll(Enumerable.Repeat("opus", 27)));
    }
}
