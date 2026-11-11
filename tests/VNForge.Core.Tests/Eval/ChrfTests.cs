using VNForge.Core.Eval;

namespace VNForge.Core.Tests.Eval;

public sealed class ChrfTests
{

    [Fact]
    public void Vazio_contra_cheio_vale_zero_e_vazio_contra_vazio_vale_100()
    {
        Assert.Equal(0.0, Chrf.Score("texto", ""));
        Assert.Equal(0.0, Chrf.Score("", "texto"));
        Assert.Equal(100.0, Chrf.Score("", null));
    }

    [Fact]
    public void O_chrf_do_conjunto_soma_as_contagens_e_da_mais_peso_as_linhas_longas()
    {
        const string longRef = "Os dois chegaram cinco minutos atrasados hoje cedo";
        (string?, string?)[] pairs = [("Oi", "Oi"), (longRef, "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")];

        var corpus = Chrf.Corpus(pairs);
        var lineMean = pairs.Average(p => Chrf.Score(p.Item1, p.Item2));

        Assert.Equal(50.0, lineMean, 6);
        Assert.InRange(corpus, 0, 20);
    }
}
