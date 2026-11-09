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
    public void Estima_cada_configuracao_sobre_as_linhas_da_amostra()
    {
        var estimate = EvalEstimator.Estimate(_project.Folder, [Opus, Haiku], 100, 1, 30, null);

        Assert.Equal(20, estimate.SampleLines);
        Assert.Equal(20, estimate.GoldLines);
        Assert.Equal(20, estimate.Configs[0].Estimate.Lines);
        Assert.True(estimate.Configs[0].Dollars > estimate.Configs[1].Dollars);
        Assert.Equal(estimate.Configs.Sum(c => c.Dollars), estimate.Dollars);
    }

    [Fact]
    public void Contexto_maior_custa_mais_que_contexto_zero()
    {
        var estimate = EvalEstimator.Estimate(_project.Folder, [Opus, Narrow], 100, 1, 30, null);

        Assert.True(estimate.Configs[0].Estimate.Usage.Input > estimate.Configs[1].Estimate.Usage.Input);
    }

    [Fact]
    public void O_juiz_soma_ao_custo_de_cada_configuracao()
    {
        var without = EvalEstimator.Estimate(_project.Folder, [Opus], 100, 1, 30, null);
        var with = EvalEstimator.Estimate(_project.Folder, [Opus], 100, 1, 30, ClaudeModels.Sonnet);

        Assert.Equal(0m, without.Configs[0].JudgeDollars);
        Assert.True(with.Configs[0].JudgeDollars > 0m);
        Assert.True(with.Dollars > without.Dollars);
    }

    [Fact]
    public void Amostra_menor_estima_menos_linhas()
    {
        var estimate = EvalEstimator.Estimate(_project.Folder, [Opus], 5, 1, 30, null);

        Assert.Equal(5, estimate.SampleLines);
        Assert.Equal(5, estimate.Configs[0].Estimate.Lines);
    }

    [Fact]
    public void Configuracao_invalida_e_recusada()
    {
        Assert.Throws<EvalException>(() => EvalEstimator.Estimate(_project.Folder, [Opus with { Model = "x" }], 5, 1, 30, null));
    }
}
