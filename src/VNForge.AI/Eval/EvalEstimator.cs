using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.AI.Eval;

/// <summary>A estimativa de uma configuração: o que as linhas da amostra custariam a ela.</summary>
public sealed record EvalConfigEstimate(EvalConfig Config, CostEstimate Estimate, decimal JudgeDollars)
{
    public decimal Dollars => Estimate.Dollars + JudgeDollars;
}

/// <summary>O custo aproximado de uma rodada, antes de rodar.</summary>
/// <param name="SampleLines">As linhas que cada configuração traduz.</param>
/// <param name="GoldLines">Quantas linhas aprovadas o projeto tem.</param>
public sealed record EvalEstimate(int SampleLines, int GoldLines, IReadOnlyList<EvalConfigEstimate> Configs)
{
    public decimal Dollars => Configs.Sum(c => c.Dollars);
}

/// <summary>
/// Estima o custo de uma rodada sem chamar a API, com a mesma heurística de <see cref="CostEstimator"/>: cada configuração monta os lotes que montaria de verdade
/// (com o contexto dela) e o juiz soma o original e a tradução de cada linha. É uma ordem de grandeza; o custo real vem do relatório da rodada.
/// </summary>
public static class EvalEstimator
{
    private const int JudgeLineOverhead = 30;
    private const int JudgeOutputPerLine = 12;
    private const int JudgeSystemTokens = 350;

    public static EvalEstimate Estimate(ProjectFolder project, IReadOnlyList<EvalConfig> configs, int sampleSize, int seed, int batchSize, string? judgeModel)
    {
        ArgumentNullException.ThrowIfNull(configs);
        EvalRunner.Validate(configs, hasBible: true);
        var set = EvalSampleSet.Load(project, sampleSize, seed, batchSize);
        var judgeDollars = judgeModel is null ? 0m : JudgeDollars(set, judgeModel, batchSize);
        var estimates = configs
            .Select(config =>
            {
                var effort = ClaudeEffortText.ParseOrDefault(config.Effort);
                var batches = set.Blocks.Select(block => set.BuildBatch(config, block));
                return new EvalConfigEstimate(config, CostEstimator.EstimateBatches(batches, config.Model, null, effort), judgeDollars);
            })
            .ToList();
        return new EvalEstimate(set.Lines.Count, set.GoldLines, estimates);
    }

    private static decimal JudgeDollars(EvalSampleSet set, string judgeModel, int batchSize)
    {
        var batches = (int)Math.Ceiling(set.Sample.Count / (double)batchSize);
        var input = set.Sample.Sum(g => CostEstimator.Tokens(g.Source) + CostEstimator.Tokens(g.Reference) + JudgeLineOverhead) + (long)batches * JudgeSystemTokens;
        var output = (long)set.Sample.Count * JudgeOutputPerLine;
        return ModelPricing.For(judgeModel).Cost(new TokenUsage(input, output));
    }
}
