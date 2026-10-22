using System.Reflection;

namespace VNForge.Engines.YuRis.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Assembly_referenciado_carrega()
    {
        var assembly = Assembly.Load("VNForge.Engines.YuRis");

        Assert.Equal("VNForge.Engines.YuRis", assembly.GetName().Name);
    }
}
