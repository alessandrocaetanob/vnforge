using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>
/// Estimativa da passada de editor, na mesma heurística de tokens do <see cref="CostEstimator"/>. A entrada é a cena toda (original e tradução de cada linha) mais as instruções;
/// a saída é o raciocínio do modelo e os ajustes, que se supõe cobrirem <see cref="AdjustedShare"/> das linhas. É uma ordem de grandeza: o custo real vem do uso que a API devolve.
/// </summary>
public static class EditorPassCost
{
    /// <summary>A fração das linhas em que se supõe que o editor proponha um ajuste. Chute de partida; o banco de provas (#529) é quem mede.</summary>
    public const double AdjustedShare = 0.2;

    private const double TranslationPerSourceToken = 1.5;
    private const int InstructionTokens = 420;
    private const int PerLineInputOverhead = 24;
    private const int PerAdjustmentOverhead = 45;
    private const int ReasonTokens = 30;

    /// <summary>
    /// O custo de uma passada sobre <paramref name="scenes"/>, um pedido por cena (ou por fatia de <see cref="EditorPassRunner.MaxLinesPerRequest"/> linhas). Uma linha ainda sem tradução (a estimativa antes de traduzir) conta com a tradução que
    /// o <see cref="CostEstimator"/> espera para ela.
    /// </summary>
    public static CostEstimate Estimate(IReadOnlyList<IReadOnlyList<TextUnit>> scenes, string model, ClaudeEffort effort = ClaudeEffort.Medium)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var price = ModelPricing.For(model);
        long input = 0, output = 0;
        var lineCount = 0;
        var calls = 0;
        foreach (var scene in scenes.Where(s => s.Count > 0))
        {
            calls += (int)Math.Ceiling(scene.Count / (double)EditorPassRunner.MaxLinesPerRequest);
            foreach (var unit in scene)
            {
                var source = CostEstimator.Tokens(unit.Source) + CostEstimator.Tokens(unit.Speaker ?? string.Empty);
                var translation = unit.HasTranslation ? CostEstimator.Tokens(unit.Translation) : (long)Math.Ceiling(source * TranslationPerSourceToken);
                input += source + translation + PerLineInputOverhead;
                output += (long)Math.Ceiling(AdjustedShare * (translation + ReasonTokens + PerAdjustmentOverhead));
                lineCount++;
            }
        }

        input += calls * (long)InstructionTokens;
        output += calls * Reasoning(effort);
        var usage = new TokenUsage(input, output);
        return new CostEstimate(lineCount, 0, usage, lineCount == 0 ? 0m : price.Cost(usage));
    }

    private static long Reasoning(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => 200,
        ClaudeEffort.Medium => 800,
        ClaudeEffort.High => 2500,
        _ => 6000,
    };
}
