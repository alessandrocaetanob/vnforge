using System.Reflection;
using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(IEngine).Assembly;

    [Fact]
    public void Core_nao_referencia_engines_nem_ia()
    {
        var proibidas = Core.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(IsProibido)
            .ToList();

        Assert.True(
            proibidas.Count == 0,
            $"VNForge.Core não pode depender de engine nem de IA, mas referencia: {string.Join(", ", proibidas)}");
    }

    [Fact]
    public void Core_nao_expoe_tipos_de_yuris()
    {
        var tipos = Core.GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .Where(n => n.Contains("YuRis", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Ypf", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Ystb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(tipos.Count == 0, $"Tipos específicos de YU-RIS no Core: {string.Join(", ", tipos)}");
    }

    [Theory]
    [InlineData("VNForge.Engines.YuRis", true)]
    [InlineData("VNForge.Engines.KiriKiri", true)]
    [InlineData("VNForge.AI", true)]
    [InlineData("VNForge.Core", false)]
    [InlineData("VNForge.AIExtras", false)]
    [InlineData("System.Runtime", false)]
    [InlineData(null, false)]
    public void IsProibido_reconhece_os_assemblies_vedados(string? nome, bool esperado)
    {
        Assert.Equal(esperado, IsProibido(nome));
    }

    private static bool IsProibido(string? nome) =>
        nome is not null
        && (nome.StartsWith("VNForge.Engines.", StringComparison.Ordinal) || nome == "VNForge.AI");
}
