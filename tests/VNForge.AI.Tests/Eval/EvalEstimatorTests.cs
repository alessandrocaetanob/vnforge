using VNForge.AI.Claude;
using VNForge.AI.Eval;
using VNForge.Core.Eval;

namespace VNForge.AI.Tests.Eval;

public sealed class EvalEstimatorTests : IDisposable
{
    private readonly EvalProject _project = new();

    public void Dispose() => _project.Dispose();

    private static readonly EvalConfig Opus = new("A", ClaudeModels.Opus, "high", 15, false);
    private static readonly EvalConfig Haiku = new("B", ClaudeModels.Haiku, "low", 15, false);
    private static readonly EvalConfig Narrow = new("C", ClaudeModels.Opus, "high", 0, false);

    [Fact]
    public void O_juiz_soma_ao_custo_de_cada_configuracao()
    {
        var without = EvalEstimator.Estimate(_project.Folder, [Opus], 100, 1, 30, null);
        var with = EvalEstimator.Estimate(_project.Folder, [Opus], 100, 1, 30, ClaudeModels.Sonnet);

        Assert.Equal(0m, without.Configs[0].JudgeDollars);
        Assert.True(with.Configs[0].JudgeDollars > 0m);
        Assert.True(with.Dollars > without.Dollars);
    }
}
