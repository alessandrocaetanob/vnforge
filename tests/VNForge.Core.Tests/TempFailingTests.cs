namespace VNForge.Core.Tests;

public class TempFailingTests
{
    [Fact]
    public void Falha_de_proposito()
    {
        Assert.Equal(1, 2);
    }
}
