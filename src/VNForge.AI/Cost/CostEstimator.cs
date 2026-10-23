using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>Estimativa do <c>--dry-run</c>: o que a execução gastaria, sem chamar a API.</summary>
/// <param name="Lines">Linhas que iriam à API.</param>
/// <param name="CachedLines">Linhas já no cache, que não custam nada.</param>
public sealed record CostEstimate(int Lines, int CachedLines, TokenUsage Usage, decimal Dollars);

/// <summary>
/// Estima tokens por heurística, porque a contagem exata exige chamar a API: caractere fora do ASCII (japonês) conta 1
/// token, ASCII conta 1 a cada 4 caracteres, e cada linha soma uma sobrecarga fixa do JSON. A saída em português
/// gasta cerca de 1,5 token por token do original. É uma ordem de grandeza; o custo real vem do <see cref="UsageLedger"/>.
/// </summary>
public static class CostEstimator
{
    private const int PerLineOverhead = 20;
    private const int PerLineOutputOverhead = 8;
    private const int SystemOverhead = 300;
    private const double OutputPerSourceToken = 1.5;

    /// <param name="context">O mesmo contexto que o <see cref="CachingTranslationProvider"/> usa nas chaves (<see cref="TranslationCache.ContextFor"/>).</param>
    public static CostEstimate Estimate(IEnumerable<TextUnit> lines, string model, TranslationCache? cache = null, string context = "")
    {
        ArgumentNullException.ThrowIfNull(lines);
        var price = ModelPricing.For(model);
        long input = 0, sourceTokens = 0;
        int pending = 0, cached = 0;

        foreach (var unit in lines)
        {
            if (cache is not null && cache.TryGet(TranslationCache.KeyFor(unit, context), out _))
            {
                cached++;
                continue;
            }

            pending++;
            var tokens = Tokens(unit.Source) + Tokens(unit.Speaker ?? string.Empty);
            sourceTokens += tokens;
            input += tokens + PerLineOverhead;
        }

        if (pending > 0)
            input += SystemOverhead;
        var usage = new TokenUsage(input, (long)Math.Ceiling(sourceTokens * OutputPerSourceToken) + (long)pending * PerLineOutputOverhead);
        return new CostEstimate(pending, cached, usage, price.Cost(usage));
    }

    /// <summary>
    /// Estima a partir dos lotes que a execução montaria, para que as chaves de cache usem o mesmo contexto (modelo,
    /// estilo, glossário, personagens) do <see cref="CachingTranslationProvider"/> e o <c>--dry-run</c> não superestime.
    /// </summary>
    public static CostEstimate EstimateBatches(IEnumerable<TranslationBatch> batches, string model, TranslationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(batches);
        var total = new CostEstimate(0, 0, default, 0m);
        foreach (var batch in batches)
        {
            var e = Estimate(batch.Lines, model, cache, TranslationCache.ContextFor(model, batch));
            total = new CostEstimate(total.Lines + e.Lines, total.CachedLines + e.CachedLines, total.Usage + e.Usage, total.Dollars + e.Dollars);
        }

        return total;
    }

    private static long Tokens(string text)
    {
        long ascii = 0, other = 0;
        foreach (var c in text)
        {
            if (c < 128) ascii++;
            else other++;
        }

        return other + (ascii + 3) / 4;
    }
}
