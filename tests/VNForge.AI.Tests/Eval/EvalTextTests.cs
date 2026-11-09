using VNForge.AI.Eval;

namespace VNForge.AI.Tests.Eval;

public sealed class EvalTextTests
{
    [Fact]
    public void Formata_reais_e_notas_com_virgula()
    {
        Assert.Equal("R$ 156,70", EvalText.Brl(156.7m));
        Assert.Equal("R$ 1.234,50", EvalText.Brl(1234.5m));
        Assert.Equal("61,4", EvalText.Score(61.4));
        Assert.Equal("47,0", EvalText.Score(47));
    }

    [Theory]
    [InlineData(10, "menos de 1 min")]
    [InlineData(41 * 60, "41 min")]
    [InlineData(65 * 60, "1 h 05 min")]
    [InlineData(120 * 60, "2 h 00 min")]
    public void Formata_a_duracao_em_minutos(int seconds, string expected)
    {
        Assert.Equal(expected, EvalText.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Escolha_as_cegas_diz_quantos_pares_ou_que_a_configuracao_nao_rodou()
    {
        Assert.Equal("9 de 12", EvalText.Blind((9, 12), true));
        Assert.Equal("não rodou", EvalText.Blind((0, 0), true));
        Assert.Equal("sem rodada", EvalText.Blind((0, 0), false));
    }

    [Fact]
    public void Conta_linhas_no_singular_e_com_milhar()
    {
        Assert.Equal("1 linha", EvalText.CountLines(1));
        Assert.Equal("18.320 linhas", EvalText.CountLines(18320));
    }
}
