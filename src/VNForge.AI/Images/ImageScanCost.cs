using VNForge.AI.Cost;
using VNForge.Core.Images;

namespace VNForge.AI.Images;

/// <summary>
/// Estimativa do que a leitura das imagens custa, antes de a pessoa pedir. Todos os modelos de <see cref="ClaudeModels"/> são da geração 5.x, no nível
/// de alta resolução da API: a imagem custa ⌈largura / 28⌉ x ⌈altura / 28⌉ tokens, e a que passa de 2576 pixels no lado maior ou de 4784 tokens é
/// reduzida, mantendo a proporção, até caber (platform.claude.com/docs/en/build-with-claude/vision, "Resolution and token cost", e
/// vision-coordinates, "How Claude resizes and pads images"). O resto é a instrução e a saída. É uma ordem de grandeza; o custo real vem do uso que a API devolve.
/// </summary>
public static class ImageScanCost
{
    private const int Patch = 28;
    private const int MaxEdge = 2576;
    private const int MaxImageTokens = 4784;
    private const int PromptTokens = 330;

    // A saída é o JSON { hasText, text } (uns 80 tokens para uma tela de menu) mais o thinking adaptativo, que a API cobra como saída. Com effort Low o
    // thinking é curto, mas Opus e Fable o mantêm sempre ligado, e o pedido só é cortado em MaxTokens 2000. Reservar 500 tokens por imagem (um quarto
    // do teto) cobre o thinking comum sem chegar ao pior caso, e errar para cima é o lado seguro de uma estimativa que decide se o limite de gasto deixa seguir.
    private const int OutputTokensPerImage = 500;

    public static int ImageTokens(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var (w, h) = Resized(width, height);
        return Patches(w, h);
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

    private static int Patches(int width, int height) => (int)Math.Ceiling(width / (double)Patch) * (int)Math.Ceiling(height / (double)Patch);

    private static bool Fits(int width, int height) =>
        (int)Math.Ceiling(width / (double)Patch) * Patch <= MaxEdge
        && (int)Math.Ceiling(height / (double)Patch) * Patch <= MaxEdge
        && Patches(width, height) <= MaxImageTokens;

    /// <summary>O tamanho a que a API reduz a imagem (a implementação de referência da documentação); o mesmo tamanho quando ela já cabe.</summary>
    private static (int Width, int Height) Resized(int width, int height)
    {
        if (Fits(width, height))
        {
            return (width, height);
        }

        if (height > width)
        {
            var (rw, rh) = Resized(height, width);
            return (rh, rw);
        }

        var aspect = width / (double)height;
        int low = 1, high = width;
        while (low + 1 < high)
        {
            var mid = (low + high) / 2;
            if (Fits(mid, Math.Max((int)Math.Round(mid / aspect), 1)))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low, Math.Max((int)Math.Round(low / aspect), 1));
    }
}
