using VNForge.App.Services;

namespace VNForge.App.Tests;

public class TechnicalDetailsMemoryTests
{

    [Theory]
    [InlineData("{ não é json")]
    [InlineData("[true, false]")]
    [InlineData("{\"build\": \"sim\", \"start\": 3}")]
    public void Arquivo_ilegivel_vale_como_nada_lembrado_e_a_proxima_gravacao_o_substitui(string content)
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        File.WriteAllText(Path.Combine(config, "technical-details.json"), content);

        var memory = new TechnicalDetailsMemory(config);
        Assert.Null(memory.Get("build"));
        Assert.Null(memory.Get("start"));

        memory.Set("build", true);
        Assert.True(new TechnicalDetailsMemory(config).Get("build"));
    }

    [Fact]
    public void Gravacao_que_falha_nao_derruba_e_o_estado_vale_ate_o_app_fechar()
    {
        using var temp = new TempFolder();
        var blocker = Path.Combine(temp.Path, "arquivo");
        File.WriteAllText(blocker, "ocupa o lugar da pasta");

        var memory = new TechnicalDetailsMemory(Path.Combine(blocker, "config"));
        memory.Set("build", true);

        Assert.True(memory.Get("build"));
    }
}
