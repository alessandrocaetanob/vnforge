using VNForge.App.Services;

namespace VNForge.App.Tests;

[Collection("TechnicalDetailsMemory")]
public class TechnicalDetailsMemoryTests
{
    [Fact]
    public void Sem_pasta_lembra_so_em_memoria_e_o_que_ninguem_mexeu_fica_sem_resposta()
    {
        var memory = new TechnicalDetailsMemory();

        Assert.Null(memory.Get("build"));
        memory.Set("build", true);
        memory.Set("start", false);

        Assert.True(memory.Get("build"));
        Assert.False(memory.Get("start"));
        Assert.Null(memory.Get("conference"));
    }

    [Fact]
    public void Com_pasta_grava_nas_preferencias_e_o_proximo_app_le_de_volta()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");

        var first = new TechnicalDetailsMemory(config);
        first.Set("build", true);
        first.Set("conference", true);
        first.Set("conference", false);

        var second = new TechnicalDetailsMemory(config);
        Assert.True(second.Get("build"));
        Assert.False(second.Get("conference"));
        Assert.Null(second.Get("start"));
    }

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

    [Fact]
    public void Install_troca_a_memoria_atual_e_descartar_devolve_a_anterior()
    {
        var before = TechnicalDetailsMemory.Current;
        var other = new TechnicalDetailsMemory();

        using (TechnicalDetailsMemory.Install(other))
        {
            Assert.Same(other, TechnicalDetailsMemory.Current);
        }

        Assert.Same(before, TechnicalDetailsMemory.Current);
    }
}
