using VNForge.App.Services;

namespace VNForge.App.Tests;

public class TechnicalDetailsMemoryTests
{

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
