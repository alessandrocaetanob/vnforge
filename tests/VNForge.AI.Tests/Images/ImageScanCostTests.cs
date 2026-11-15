using VNForge.AI.Claude;
using VNForge.AI.Images;
using VNForge.Core.Images;

namespace VNForge.AI.Tests.Images;

[Trait("Risco", "R5")]
public sealed class ImageScanCostTests
{
    private static ImageCandidate Candidate(int width, int height) =>
        new("ui.ypf", $"{width}x{height}.png", new ImageInfo(ImageFormat.Png, width, height), 1000, $"hash{width}x{height}");

    [Fact]
    public void Estimativa_soma_entrada_e_saida_e_cobra_pelo_preco_do_modelo()
    {
        var estimate = ImageScanCost.Estimate([Candidate(750, 100), Candidate(300, 200)], ClaudeModels.Haiku);

        Assert.Equal(2, estimate.Lines);
        Assert.True(estimate.Usage.Input > 100 + 80, estimate.Usage.ToString());
        Assert.True(estimate.Usage.Output > 0);
        Assert.True(estimate.Dollars > 0m);
        Assert.True(estimate.Dollars < 0.01m, "duas imagens pequenas no Haiku custam menos de um centavo");
    }
}
