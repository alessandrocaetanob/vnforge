using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public class TitleSimilarityTests
{

    [Fact]
    public void Titulo_parecido_pontua_por_pares_de_caracteres()
    {
        Assert.InRange(TitleSimilarity.Between("Sakura no Uta", "Sakura no Uta Remastered"), 0.7, 0.9);
        Assert.InRange(TitleSimilarity.Between("Sakura no Utta", "Sakura no Uta"), 0.85, 0.99);
        Assert.Equal(0, TitleSimilarity.Between("!!!", "Clannad"));
    }

    [Fact]
    public void Percentual_nao_depende_da_cultura()
    {
        Assert.Equal("92%", TitleSimilarity.Percent(0.92));
    }
}
