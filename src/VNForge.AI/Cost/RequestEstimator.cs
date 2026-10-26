using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>
/// Estima em reais o custo de pedir a tradução de um conjunto de linhas de uma <see cref="LineSelection"/>: divide em lotes do tamanho da
/// execução, monta cada lote com o glossário, os personagens, o <c>style.md</c> e as falas de contexto que ele levaria, e soma
/// (<see cref="CostEstimator.EstimateBatch"/>). É o valor do <see cref="BudgetGuard"/> e o do aviso do app e do <c>vnf</c>, aproximado.
/// </summary>
public sealed class RequestEstimator(
    LineSelection selection,
    TranslationBatchBuilder builder,
    string model,
    ClaudeEffort effort,
    int batchSize,
    ExchangeRate rate,
    TranslationCache? cache = null)
{
    public CostEstimate Estimate(IReadOnlyList<TextUnit> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return CostEstimator.EstimateBatches(lines.Chunk(batchSize).Select(chunk => selection.BuildBatch(builder, chunk)), model, cache, effort);
    }

    public decimal Brl(IReadOnlyList<TextUnit> lines) => rate.ToBrl(Estimate(lines).Dollars);
}
