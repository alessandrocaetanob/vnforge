using System.Globalization;
using VNForge.App.Views;

namespace VNForge.App.Tests;

/// <summary>A largura do cartão do dicionário (#538): os 370 px do quadro, e nunca mais larga que o original em que ele se alinha.</summary>
public sealed class WordCardWidthTests
{
    [Theory]
    [InlineData(428, 370)]
    [InlineData(370, 370)]
    [InlineData(335, 335)]
    [InlineData(0, 370)]
    public void O_cartao_tem_a_largura_do_quadro_ou_a_do_original_quando_ele_e_mais_estreito(double original, double esperada)
    {
        var width = (double)WordCardWidth.Fit.Convert(original, typeof(double), null, CultureInfo.InvariantCulture)!;

        Assert.Equal(esperada, width);
    }
}
