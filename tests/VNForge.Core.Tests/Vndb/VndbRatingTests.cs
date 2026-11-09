using System.Globalization;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>A nota do VNDB (campo <c>rating</c>, de 10 a 100) como o quadro a escreve: "nota 7,9".</summary>
public sealed class VndbRatingTests
{
    [Theory]
    [InlineData(78.6, "nota 7,9")]
    [InlineData(72, "nota 7,2")]
    [InlineData(100, "nota 10")]
    [InlineData(65, "nota 6,5")]
    [InlineData(10, "nota 1")]
    public void A_nota_sai_com_uma_casa_e_virgula(double rating, string expected) =>
        Assert.Equal(expected, VndbRating.Label(rating));

    [Fact]
    public void A_formatacao_nao_depende_da_cultura_da_maquina()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("nota 7,9", VndbRating.Label(78.6));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    public void Sem_voto_nao_ha_nota(double? rating) => Assert.Null(VndbRating.Label(rating));

    [Fact]
    public void O_jogo_guarda_a_nota_e_o_registro_antigo_continua_sem_ela()
    {
        Assert.Null(VndbSamples.Game().Rating);
        Assert.Equal(78.6, (VndbSamples.Game() with { Rating = 78.6 }).Rating);
    }
}
