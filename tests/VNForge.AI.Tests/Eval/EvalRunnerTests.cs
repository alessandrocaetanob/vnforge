using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;

namespace VNForge.AI.Tests.Eval;

[Trait("Risco", "R5")]
public sealed class EvalRunnerTests : IDisposable
{
    private readonly EvalProject _project = new();

    public void Dispose() => _project.Dispose();

    private static readonly EvalConfig Sonnet = new("A", ClaudeModels.Sonnet, "medium", 2, false);
    private static readonly EvalConfig Haiku = new("B", ClaudeModels.Haiku, "low", 5, false);

    private static TranslationRunOptions Quick { get; } = new()
    {
        MaxRetries = 0,
        InitialBackoff = TimeSpan.Zero,
        Delay = static (_, _) => Task.CompletedTask,
    };

    private EvalRunRequest Request(Func<EvalConfig, EvalProvider> providers, params EvalConfig[] configs) =>
        new(_project.Folder, configs, 100, 3, providers) { RunOptions = Quick };

    private static Func<EvalConfig, EvalProvider> Same(ITranslationProvider provider, ClaudeUsage? usage = null) =>
        _ => new EvalProvider(provider, () => usage);

    [Fact]
    public async Task Configuracao_com_biblia_e_recusada_antes_de_gastar_quando_nao_ha_biblia()
    {
        var calls = 0;
        var request = Request(c =>
        {
            calls++;
            return new EvalProvider(EvalProject.Perfect(), () => null);
        }, Sonnet with { WithBible = true });

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("A pede a bíblia do jogo", error.Message);
        Assert.Equal(0, calls);
        Assert.Null(EvalStore.LoadLatest(_project.Folder));
    }

    [Fact]
    public async Task O_juiz_tambem_para_antes_do_lote_que_passaria_do_limite()
    {
        using var project = new EvalProject(goldCount: 100);
        var judge = new FakeJudgeProvider { UsagePerCall = new ClaudeUsage(500_000, 0, 0, 0) };
        var request = new EvalRunRequest(project.Folder, [Sonnet], 100, 3, Same(EvalProject.Perfect()))
        {
            RunOptions = Quick,
            Judge = judge,
            JudgeModel = ClaudeModels.Haiku,
            Rate = new ExchangeRate(5m),
            Limits = new BudgetLimits(0.30m, null),
        };

        var error = await Assert.ThrowsAsync<EvalException>(() => EvalRunner.RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("limite", error.Message);
        Assert.Equal(2, judge.ReceivedBatches.Count);
        Assert.Equal(0.10m, AiUsageLog.Load(project.Folder).Dollars);
    }

    private static int Index(VNForge.Core.Text.TextUnit unit) => int.Parse(unit.Id[(unit.Id.IndexOf(':', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
