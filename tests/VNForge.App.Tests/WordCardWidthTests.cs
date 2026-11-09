using System.Globalization;
using VNForge.App.Views;

namespace VNForge.App.Tests;

/// <summary>A largura do cartão do dicionário (#538): 360 a 372 px como nos quadros, e nunca mais larga que o original em que ele se alinha.</summary>
public sealed class WordCardWidthTests
{
    [Theory]
    [InlineData(428, 360, 372)]
    [InlineData(365, 360, 365)]
    [InlineData(335, 335, 335)]
    [InlineData(0, 360, 372)]
    public void Os_limites_sao_os_dos_dois_quadros_ou_a_largura_do_original_quando_ele_e_mais_estreito(double original, double minimo, double maximo)
    {
        Assert.Equal(minimo, (double)WordCardWidth.FitMin.Convert(original, typeof(double), null, CultureInfo.InvariantCulture)!);
        Assert.Equal(maximo, (double)WordCardWidth.FitMax.Convert(original, typeof(double), null, CultureInfo.InvariantCulture)!);
    }
}
