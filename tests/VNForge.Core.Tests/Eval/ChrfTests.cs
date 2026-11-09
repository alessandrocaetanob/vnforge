using VNForge.Core.Eval;

namespace VNForge.Core.Tests.Eval;

public sealed class ChrfTests
{
    [Fact]
    public void Textos_iguais_valem_100()
    {
        Assert.Equal(100.0, Chrf.Score("Os dois chegaram atrasados.", "Os dois chegaram atrasados."), 6);
    }

    [Fact]
    public void Espacos_nao_contam()
    {
        Assert.Equal(100.0, Chrf.Score("Os dois chegaram", "Osdois  chegaram"), 6);
    }

    [Fact]
    public void Texto_sem_nada_em_comum_vale_zero()
    {
        Assert.Equal(0.0, Chrf.Score("abcdef", "xyzxyz"), 6);
    }

    [Fact]
    public void Vazio_contra_cheio_vale_zero_e_vazio_contra_vazio_vale_100()
    {
        Assert.Equal(0.0, Chrf.Score("texto", ""));
        Assert.Equal(0.0, Chrf.Score("", "texto"));
        Assert.Equal(100.0, Chrf.Score("", null));
    }

    [Fact]
    public void Texto_parecido_pontua_entre_zero_e_cem_e_acima_do_menos_parecido()
    {
        const string reference = "Aizawa, Sakura. Vocês dois chegaram cinco minutos atrasados.";
        var close = Chrf.Score(reference, "Aizawa, Sakura. Os dois chegaram cinco minutos atrasados.");
        var far = Chrf.Score(reference, "Aizawa, Sakura. Atrasados.");

        Assert.InRange(close, 50, 100);
        Assert.True(close > far);
        Assert.InRange(far, 0, 100);
    }

    [Fact]
    public void Texto_curto_demais_para_os_n_gramas_maiores_ainda_pontua()
    {
        Assert.Equal(100.0, Chrf.Score("Oi", "Oi"), 6);
        Assert.True(Chrf.Score("Oi", "Oi!") > 0);
    }

    [Fact]
    public void Calcula_o_valor_conhecido_de_um_caso_pequeno()
    {
        // ref "abc", hyp "abd": n=1 casa 2 de 3; n=2 casa 1 de 2; n=3 casa 0 de 1.
        // P = R = (2/3 + 1/2 + 0) / 3 = 0,38889; com P = R o F-beta é o próprio valor.
        Assert.Equal(100.0 * (2.0 / 3 + 0.5 + 0) / 3, Chrf.Score("abc", "abd"), 6);
    }
}
