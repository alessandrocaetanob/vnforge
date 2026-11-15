using VNForge.AI.Bible;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

[Trait("Risco", "R5")]
[Trait("Risco", "R7")]
public sealed class BibleRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public BibleRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly (string Chapter, string[] Keys)[] ExpectedSceneCalls = [("Manhã", ["yst1", "yst2"]), ("Manhã", ["yst3"]), ("Festival", ["yst4"])];

    private static BibleRunOptions NoWait() => new() { Delay = (_, _) => Task.CompletedTask, ScenesPerCall = 2, Model = ClaudeModels.Opus };

    private static IReadOnlyList<BibleChapterInput> TwoChapters() =>
    [
        Chapter("Manhã", 1, Scene("yst1", "manha", "おはよう"), Scene("yst2", "tarde", "ただいま"), Scene("yst3", "noite", "おやすみ")),
        Chapter("Festival", 2, Scene("yst4", "festa", "祭りだ")),
    ];

    [Fact]
    public async Task Bible_json_ilegivel_para_antes_de_pagar_e_nao_toca_o_backup()
    {
        var project = NewProject(_root);
        project.SaveBible(GameBible.From([new BibleScene("yst1") { Summary = "Boa" }]));
        project.KeepPreviousBible();
        var backup = File.ReadAllText(project.PreviousBiblePath);
        File.WriteAllText(project.BiblePath, "{ isto não é json");
        var provider = new FakeBibleProvider();

        await Assert.ThrowsAsync<GlossaryFileException>(() => new BibleRunner(provider, NoWait()).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken));

        Assert.Empty(provider.SceneCalls);
        Assert.Equal(backup, File.ReadAllText(project.PreviousBiblePath));
    }

    [Fact]
    public async Task Para_quando_o_gasto_chega_ao_limite()
    {
        var project = NewProject(_root);
        var spent = 0m;
        var guard = new BudgetGuard(new BudgetLimits(10m, null), () => spent, _ => 0m, null);
        var provider = new FakeBibleProvider(describe: (_, key) =>
        {
            spent = 10m;
            return new BibleScene(key) { Name = "x", Summary = "y" };
        });

        var result = await new BibleRunner(provider, NoWait() with { Budget = guard }).RunAsync(project, TwoChapters(), null, TestContext.Current.CancellationToken);

        Assert.Equal(BudgetStop.LimitReached, result.BudgetStop);
        Assert.Equal(1, result.ChaptersRead);
        Assert.Empty(provider.OverviewCalls);
    }

    // Progress<T> entrega os avisos no pool de threads; com o runner carregado, nem todos chegavam antes da asserção.
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
