using VNForge.AI.Cost;
using VNForge.Core.Images;

namespace VNForge.AI.Images;

/// <summary>
/// Estimativa do que a leitura das imagens custa, antes de a pessoa pedir. A API cobra a imagem por tamanho: largura vezes altura sobre 750 tokens,
/// com o lado maior reduzido a 1568 pixels e um teto de cerca de 1600 tokens por imagem (docs.anthropic.com, "Vision"). O resto é a instrução e
/// uma saída curta. É uma ordem de grandeza; o custo real vem do uso que a API devolve.
/// </summary>
public static class ImageScanCost
{
    private const int MaxSide = 1568;
    private const int MaxImageTokens = 1600;
    private const int PixelsPerToken = 750;
    private const int PromptTokens = 330;
    private const int OutputTokensPerImage = 80;

    public static int ImageTokens(int width, int height)
    {
        var scale = Math.Min(1.0, MaxSide / (double)Math.Max(width, height));
        var tokens = (long)Math.Ceiling(Math.Round(width * scale) * Math.Round(height * scale) / PixelsPerToken);
        return (int)Math.Min(tokens, MaxImageTokens);
    }

    public static CostEstimate Estimate(IReadOnlyList<ImageCandidate> images, string model)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            return new CostEstimate(0, 0, default, 0m);
        }

        long input = images.Sum(i => (long)ImageTokens(i.Info.Width, i.Info.Height) + PromptTokens);
        var usage = new TokenUsage(input, images.Count * (long)OutputTokensPerImage);
        return new CostEstimate(images.Count, 0, usage, ModelPricing.For(model).Cost(usage));
    }
}
