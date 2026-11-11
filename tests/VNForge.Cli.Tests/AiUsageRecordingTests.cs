using VNForge.AI.Claude;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class AiUsageRecordingTests : IDisposable
{
    private const string UnitId = "yst00001:000001";

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;
    private readonly string _projectPath;

    public AiUsageRecordingTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText("yst00001", [new TextUnit(UnitId, TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        _cli.Cli.TranslationProviderFactory = (_, _) => new ClaudeTranslationProvider(new StubClient($$"""{"translations":[{"id":"{{UnitId}}","translation":"Texto"}]}"""));
    }

    public void Dispose() => _cli.Dispose();

    private sealed class StubClient(string json) : IClaudeMessageClient
    {
        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct) =>
            Task.FromResult(new ClaudeReply("end_turn", json, new ClaudeUsage(1_000_000, 100_000, 0, 0)));
    }

    [Fact]
    public async Task Dry_run_nao_grava_gasto()
    {
        await _cli.RunAsync(["translate", "--project", _projectPath, "--dry-run"], TestContext.Current.CancellationToken);

        Assert.Equal(AiUsageTotals.Empty, AiUsageLog.Load(_project));
        Assert.False(File.Exists(AiUsageLog.PathFor(_project)));
    }
}
