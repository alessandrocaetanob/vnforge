using System.Reflection;

namespace VNForge.AI.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Assembly_referenciado_carrega()
    {
        var assembly = Assembly.Load("VNForge.AI");

        Assert.Equal("VNForge.AI", assembly.GetName().Name);
    }
}
