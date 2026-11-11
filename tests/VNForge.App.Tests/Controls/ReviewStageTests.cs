using Avalonia;
using Avalonia.Controls;
using VNForge.App.Controls;
using VNForge.App.Tests.Screens;

namespace VNForge.App.Tests.Controls;

/// <summary>A conta do palco 16:9 e a pilha que o acompanha (<see cref="StageColumn"/>), sem janela: só medida e arranjo.</summary>
public sealed class ReviewStageTests
{
    private const double Aspect = 16d / 9d;

    [Theory]
    [InlineData(800, 600, 800)]
    [InlineData(800, 300, 300 * Aspect)]
    [InlineData(0, 300, 0)]
    [InlineData(800, 0, 0)]
    [InlineData(0, 0, 0)]
    public void Fit_cabe_nos_dois_eixos_e_nunca_passa_de_nenhum(double width, double height, double expectedWidth)
    {
        var fitted = ReviewStage.Fit(new Size(width, height));

        Assert.Equal(expectedWidth, fitted.Width, 3);
        Assert.Equal(expectedWidth / Aspect, fitted.Height, 3);
    }
}
