using System.Reflection;

namespace VNForge.Core.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Assembly_referenciado_carrega()
    {
        var assembly = Assembly.Load("VNForge.Core");

        Assert.Equal("VNForge.Core", assembly.GetName().Name);
    }
}
