using VNForge.AI.Claude;
using VNForge.AI.Images;
using VNForge.Core.Images;

namespace VNForge.AI.Tests.Images;

public sealed class ImageScanCostTests
{
    private static ImageCandidate Candidate(int width, int height) =>
        new("ui.ypf", $"{width}x{height}.png", new ImageInfo(ImageFormat.Png, width, height), 1000, $"hash{width}x{height}");

    [Fact]
    public void Imagem_comprida_e_reduzida_pelo_lado_maior_antes_de_contar()
    {
        // 8000 x 100 vira 2576 x 32: 92 x 2 blocos.
        Assert.Equal(92 * 2, ImageScanCost.ImageTokens(8000, 100));
        Assert.Equal(ImageScanCost.ImageTokens(8000, 100), ImageScanCost.ImageTokens(100, 8000));
    }

    [Fact]
    public void Sem_imagens_o_custo_e_zero()
    {
        var estimate = ImageScanCost.Estimate([], ClaudeModels.Haiku);

        Assert.Equal(0, estimate.Lines);
        Assert.Equal(0m, estimate.Dollars);
    }

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
