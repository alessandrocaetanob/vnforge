using VNForge.AI.Claude;
using VNForge.AI.Cost;

namespace VNForge.AI.Bible;

/// <summary>O que gerar a bíblia custaria, antes de rodar.</summary>
/// <param name="Usage">Os tokens estimados, separados como a API os cobra (a primeira leitura de cada capítulo escreve o cache; as outras o leem).</param>
/// <param name="RereadDollars">Quanto custa ler de novo um capítulo (a média) com o cache já vencido: uma escrita de cache e uma ou mais chamadas.</param>
public sealed record BibleCostEstimate(int Chapters, int Scenes, int Lines, TokenUsage Usage, decimal Dollars, decimal RereadDollars)
{
    public decimal Brl(ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return rate.ToBrl(Dollars);
    }

    public decimal RereadBrl(ExchangeRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return rate.ToBrl(RereadDollars);
    }
}

/// <summary>
/// Estimativa da geração da bíblia, na mesma heurística de tokens do <see cref="CostEstimator"/>. Cada capítulo vai inteiro para o cache de prompt na primeira chamada
/// (escrita, 1,25x a entrada) e as chamadas seguintes sobre ele o leem (a fração da entrada que <see cref="ModelPricing"/> lista); a resposta e o raciocínio do modelo
/// entram como saída. É uma ordem de grandeza: o custo real vem do uso que a API devolve.
/// </summary>
public static class BibleCost
{
    private const int InstructionTokens = 450;
    private const int PerCallUserTokens = 120;
    private const int PerSceneUserTokens = 25;
    private const int OutputTokensPerScene = 330;
    private const int PerSceneDigestTokens = 110;
    private const int OverviewOutputTokensPerChapter = 70;
    private const int OverviewBaseOutputTokens = 450;

    public static BibleCostEstimate Estimate(
        IReadOnlyList<BibleChapterInput> chapters, string model, int scenesPerCall = BibleRunOptions.DefaultScenesPerCall, ClaudeEffort effort = ClaudeEffort.Medium, bool includeOverview = true)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentOutOfRangeException.ThrowIfLessThan(scenesPerCall, 1);
        var total = default(TokenUsage);
        var dollars = 0m;
        var rereadSum = 0m;

        // O preço depende do tamanho do prompt de cada pedido (o Haiku cobra mais acima de 100 mil tokens), e o prompt de um capítulo é o mesmo em todas as suas chamadas.
        foreach (var chapter in chapters)
        {
            var (usage, promptTokens) = Chapter(chapter, scenesPerCall, effort);
            var cost = ModelPricing.For(model, promptTokens).Cost(usage);
            total += usage;
            dollars += cost;
            rereadSum += cost;
        }

        var scenes = chapters.Sum(c => c.Scenes.Count);
        if (includeOverview && chapters.Count > 0)
        {
            var overview = new TokenUsage(
                InstructionTokens + scenes * (long)PerSceneDigestTokens,
                OverviewBaseOutputTokens + chapters.Count * (long)OverviewOutputTokensPerChapter + Reasoning(effort));
            total += overview;
            dollars += ModelPricing.For(model, overview.Input).Cost(overview);
        }

        return new BibleCostEstimate(
            chapters.Count,
            scenes,
            chapters.Sum(c => c.LineCount),
            total,
            dollars,
            chapters.Count == 0 ? 0m : rereadSum / chapters.Count);
    }

    private static (TokenUsage Usage, long PromptTokens) Chapter(BibleChapterInput chapter, int scenesPerCall, ClaudeEffort effort)
    {
        var text = chapter.Tokens() + chapter.Characters.Sum(c => CostEstimator.Tokens(c.Source) + CostEstimator.Tokens(c.Target) + 4) + InstructionTokens;
        var calls = (int)Math.Ceiling(chapter.Scenes.Count / (double)scenesPerCall);
        var scenes = chapter.Scenes.Count;
        var usage = new TokenUsage(
            Input: calls * (long)PerCallUserTokens + scenes * (long)PerSceneUserTokens,
            Output: scenes * (long)OutputTokensPerScene + calls * Reasoning(effort),
            CacheRead: Math.Max(0, calls - 1) * text,
            CacheWrite: calls > 0 ? text : 0);
        return (usage, text + PerCallUserTokens + Math.Min(scenes, scenesPerCall) * (long)PerSceneUserTokens);
    }

    private static long Reasoning(ClaudeEffort effort) => effort switch
    {
        ClaudeEffort.Low => 300,
        ClaudeEffort.Medium => 1200,
        ClaudeEffort.High => 3500,
        _ => 8000,
    };
}
