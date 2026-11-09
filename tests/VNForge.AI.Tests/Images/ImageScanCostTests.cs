using VNForge.AI.Claude;
using VNForge.AI.Images;
using VNForge.Core.Images;

namespace VNForge.AI.Tests.Images;

public sealed class ImageScanCostTests
{
    private static ImageCandidate Candidate(int width, int height) =>
        new("ui.ypf", $"{width}x{height}.png", new ImageInfo(ImageFormat.Png, width, height), 1000, $"hash{width}x{height}");

    [Theory]
    [InlineData(750, 100, 100)]
    [InlineData(300, 200, 80)]
    [InlineData(1000, 1000, 1334)]
    public void Tokens_da_imagem_seguem_largura_vezes_altura_sobre_750(int width, int height, int expected)
    {
        Assert.Equal(expected, ImageScanCost.ImageTokens(width, height));
    }

    [Theory]
    [InlineData(1500, 1500)]
    [InlineData(1920, 1080)]
    [InlineData(8000, 8000)]
    public void Imagem_grande_nunca_passa_do_teto_de_1600_tokens(int width, int height)
    {
        Assert.Equal(1600, ImageScanCost.ImageTokens(width, height));
    }

    [Fact]
    public void Imagem_com_o_lado_acima_de_1568_e_reduzida_antes_de_contar()
    {
        // 3136 x 100 vira 1568 x 50: 78400 / 750 = 104,5.
        Assert.Equal(105, ImageScanCost.ImageTokens(3136, 100));
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

    [Fact]
    public void Modelo_mais_caro_custa_mais()
    {
        var images = new[] { Candidate(640, 480), Candidate(1280, 720) };

        Assert.True(ImageScanCost.Estimate(images, ClaudeModels.Opus).Dollars > ImageScanCost.Estimate(images, ClaudeModels.Haiku).Dollars);
    }
}
