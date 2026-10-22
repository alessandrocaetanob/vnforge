using VNForge.Core.Engines;

namespace VNForge.Core.Tests;

public class EngineDetectionTests
{
    [Fact]
    public void NotDetected_nao_reconhece_e_nao_tem_detalhes()
    {
        var resultado = EngineDetection.NotDetected;

        Assert.False(resultado.IsMatch);
        Assert.Equal(string.Empty, resultado.Summary);
        Assert.Empty(resultado.Details);
    }

    [Fact]
    public void Matched_sem_detalhes_usa_dicionario_vazio()
    {
        var resultado = EngineDetection.Matched("parece YU-RIS");

        Assert.True(resultado.IsMatch);
        Assert.Equal("parece YU-RIS", resultado.Summary);
        Assert.Empty(resultado.Details);
    }

    [Fact]
    public void Matched_guarda_os_detalhes_informados()
    {
        var detalhes = new Dictionary<string, string> { ["ypf"] = "v500" };

        var resultado = EngineDetection.Matched("parece YU-RIS", detalhes);

        Assert.Equal("v500", resultado.Details["ypf"]);
    }

    [Fact]
    public void NotDetected_e_Matched_vazio_nao_aceitam_escrita()
    {
        Assert.True(EngineDetection.NotDetected.Details is System.Collections.Frozen.FrozenDictionary<string, string>);
        Assert.True(EngineDetection.Matched("x").Details is System.Collections.Frozen.FrozenDictionary<string, string>);
    }
}
