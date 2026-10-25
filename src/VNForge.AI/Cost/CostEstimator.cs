using System.Text;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Text;

namespace VNForge.AI.Cost;

/// <summary>Estimativa do <c>--dry-run</c>: o que a execução gastaria, sem chamar a API.</summary>
/// <param name="Lines">Linhas que iriam à API.</param>
/// <param name="CachedLines">Linhas já no cache, que não custam nada.</param>
public sealed record CostEstimate(int Lines, int CachedLines, TokenUsage Usage, decimal Dollars);

/// <summary>Uma estimativa em dólar e em real, convertida com a cotação que a pessoa informou.</summary>
public sealed record CostQuote(CostEstimate Estimate, ExchangeRate Rate)
{
    public decimal Dollars => Estimate.Dollars;

    public decimal Brl => Rate.ToBrl(Estimate.Dollars);
}

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
    private const int InstructionOverhead = 40;

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
    /// Estima as mesmas linhas para vários modelos (Ajustes: "estimativa para as pendentes"). Os tokens são os mesmos em todos; só o
    /// preço muda, então a contagem roda uma vez. A ordem do resultado é a de <paramref name="models"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Modelo sem preço na tabela.</exception>
    public static IReadOnlyDictionary<string, CostEstimate> EstimateForModels(IEnumerable<TextUnit> lines, IEnumerable<string> models, TranslationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        var names = models.ToList();
        var prices = names.Select(m => (Model: m, Price: ModelPricing.For(m))).ToList();
        var counted = Estimate(lines, ClaudeModels.Opus, cache);
        var result = new Dictionary<string, CostEstimate>(names.Count);
        foreach (var (model, price) in prices)
            result[model] = counted with { Dollars = price.Cost(counted.Usage) };
        return result;
    }

    /// <summary>
    /// Estima a partir dos lotes que a execução montaria, para que as chaves de cache usem o mesmo contexto (modelo,
    /// estilo, glossário, personagens) do <see cref="CachingTranslationProvider"/> e o <c>--dry-run</c> não superestime.
    /// Cada lote soma o que a chamada leva além das linhas (ver <see cref="EstimateBatch"/>).
    /// </summary>
    public static CostEstimate EstimateBatches(IEnumerable<TranslationBatch> batches, string model, TranslationCache? cache = null, ClaudeEffort effort = ClaudeEffort.Medium)
    {
        ArgumentNullException.ThrowIfNull(batches);
        var total = new CostEstimate(0, 0, default, 0m);
        foreach (var batch in batches)
        {
            var e = EstimateBatch(batch, model, effort, cache);
            total = new CostEstimate(total.Lines + e.Lines, total.CachedLines + e.CachedLines, total.Usage + e.Usage, total.Dollars + e.Dollars);
        }

        return total;
    }

    /// <summary>
    /// Estima uma chamada: as linhas pendentes do lote (as que o cache não tem) mais o que cada chamada repete, que é o sistema, as
    /// escolhas de estilo, o <c>style.md</c>, o glossário, os personagens e as falas de contexto, e o raciocínio do modelo, que a API cobra
    /// como saída. É uma ordem de grandeza: o raciocínio varia com o trecho e com o <paramref name="effort"/>. Lote sem linha pendente custa zero.
    /// </summary>
    public static CostEstimate EstimateBatch(TranslationBatch batch, string model, ClaudeEffort effort, TranslationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var lines = Estimate(batch.Lines, model, cache, TranslationCache.ContextFor(model, batch));
        if (lines.Lines == 0)
        {
            return lines;
        }

        var usage = lines.Usage + new TokenUsage(PromptContextTokens(batch), ReasoningTokens(effort));
        return lines with { Usage = usage, Dollars = ModelPricing.For(model).Cost(usage) };
    }

    private static long PromptContextTokens(TranslationBatch batch)
    {
        var sb = new StringBuilder();
        ClaudePromptParts.AppendStableContext(sb, batch);
        sb.Append(ClaudePromptParts.BatchContext(batch, "Glossário (use sempre estes termos):"));
        ClaudePromptParts.AppendPrevious(sb, batch, "Contexto anterior (não traduzir):");
        ClaudePromptParts.AppendNext(sb, batch, "Contexto seguinte (não traduzir):");
        return Tokens(sb.ToString()) + InstructionOverhead;
    }

    private static long ReasoningTokens(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => 200,
        ClaudeEffort.Medium => 800,
        ClaudeEffort.High => 2500,
        _ => 6000,
    };

    /// <summary>O valor em reais de uma estimativa, com a cotação informada pela pessoa.</summary>
    public static decimal Brl(CostEstimate estimate, ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(rate);
        return rate.ToBrl(estimate.Dollars);
    }

    public static CostQuote Quote(CostEstimate estimate, ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(rate);
        return new CostQuote(estimate, rate);
    }

    public static CostQuote Quote(IEnumerable<TextUnit> lines, string model, ExchangeRate rate, TranslationCache? cache = null, string context = "") =>
        Quote(Estimate(lines, model, cache, context), rate);

    /// <summary>Como <see cref="EstimateForModels"/>, em dólar e em real.</summary>
    public static IReadOnlyDictionary<string, CostQuote> QuoteForModels(IEnumerable<TextUnit> lines, IEnumerable<string> models, ExchangeRate rate, TranslationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return EstimateForModels(lines, models, cache).ToDictionary(p => p.Key, p => new CostQuote(p.Value, rate));
    }

    internal static long Tokens(string text)
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
