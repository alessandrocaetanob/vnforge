using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Factsheet;

namespace VNForge.AI.Sheet;

/// <summary>O que traduzir a ficha custaria, antes de rodar.</summary>
public sealed record SheetCostEstimate(int Items, int Calls, TokenUsage Usage, decimal Dollars)
{
    public decimal Brl(ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return rate.ToBrl(Dollars);
    }
}

/// <summary>
/// Estimativa da tradução da ficha, na mesma heurística de tokens do <see cref="CostEstimator"/>: cada chamada leva a instrução e os itens do lote, e devolve a tradução (o
/// português sai um pouco maior que o inglês) mais o raciocínio do modelo. É uma ordem de grandeza: o custo real vem do uso que a API devolve.
/// </summary>
public static class SheetCost
{
    private const int InstructionTokens = 420;
    private const int PerItemOverheadTokens = 28;
    private const double OutputGrowth = 1.3;

    public static SheetCostEstimate Estimate(SheetTranslationPlan plan, string model, SheetTranslationOptions? options = null, ClaudeEffort effort = ClaudeEffort.Medium)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var batches = SheetTranslationRunner.Split(plan.Items, options);
        var total = default(TokenUsage);
        var dollars = 0m;
        foreach (var batch in batches)
        {
            var source = batch.Sum(i => CostEstimator.Tokens(i.Source) + (i.Context is null ? 0 : CostEstimator.Tokens(i.Context)));
            var usage = new TokenUsage(
                Input: InstructionTokens + source + batch.Count * (long)PerItemOverheadTokens,
                Output: (long)(source * OutputGrowth) + batch.Count * (long)PerItemOverheadTokens + Reasoning(effort));
            total += usage;
            dollars += ModelPricing.For(model, usage.Input).Cost(usage);
        }

        return new SheetCostEstimate(plan.Items.Count, batches.Count, total, dollars);
    }

    private static long Reasoning(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => 300,
        ClaudeEffort.Medium => 1200,
        ClaudeEffort.High => 3500,
        _ => 8000,
    };
}
