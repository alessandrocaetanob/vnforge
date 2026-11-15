using System.Text.Json;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class TranslateBatchCommandTests : IDisposable
{
    private const string Key = "yst00001";
    private const string BatchFlag = "--batch";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly FakeMessageBatchClient _client = new();

    public TranslateBatchCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText(Key,
        [
            Unit(1, TranslationStatus.Untranslated),
            Unit(2, TranslationStatus.Approved),
            Unit(3, TranslationStatus.Untranslated),
            Unit(4, TranslationStatus.Untranslated),
        ]);
        _cli.Cli.MessageBatchClientFactory = () => _client;
        _cli.Cli.TranslationProviderFactory = (_, _) => throw new InvalidOperationException("O lote não usa a chamada de agora.");
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int n, TranslationStatus status) =>
        new($"{Key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : "pronta", status);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["translate", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private NightBatchState OnlyBatch() => Assert.Single(new NightBatchStore(_project).List());

    [Fact]
    public async Task Dry_run_com_batch_estima_a_metade_e_nao_chama_a_API()
    {
        var normal = await Run("--dry-run");
        var batch = await Run(BatchFlag, "--dry-run");

        Assert.Equal(0, batch.ExitCode);
        Assert.Contains("Estimativa", batch.Output, StringComparison.Ordinal);
        Assert.Contains("metade", batch.Output, StringComparison.Ordinal);
        Assert.Equal(0, _client.Creates);
        Assert.Equal(0, _client.Gets);
        Assert.NotEqual(normal.Output, batch.Output);
    }
}
