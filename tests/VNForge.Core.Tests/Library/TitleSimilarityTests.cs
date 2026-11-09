using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public class TitleSimilarityTests
{
    [Theory]
    [InlineData("Steins;Gate", "STEINS GATE", 1.0)]
    [InlineData("ＣＬＡＮＮＡＤ", "clannad", 1.0)]
    [InlineData("クラナド", "クラナド", 1.0)]
    [InlineData("Clannad", "Totally Different", 0.0)]
    public void Mede_igualdade_depois_de_normalizar(string a, string b, double expected)
    {
        Assert.Equal(expected, TitleSimilarity.Between(a, b), 3);
    }

    [Fact]
    public void Titulo_contido_pontua_pelo_tamanho_que_cobre()
    {
        var closer = TitleSimilarity.Between("Clannad", "Clannad Full Voice");
        var farther = TitleSimilarity.Between("Clannad", "Clannad Hikari Mitsu Hashi Heto Zenpen");

        Assert.InRange(closer, 0.6, 0.8);
        Assert.True(closer > farther);
    }

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
