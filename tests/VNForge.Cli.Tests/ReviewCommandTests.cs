using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Review;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R2")]
public sealed class ReviewCommandTests : IDisposable
{
    private const string Script1 = "yst00001";
    private const string JsonFlag = "--json";
    private const string FilesFlag = "--files";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private FakeReviewProvider _provider = new();
    private string? _requestedModel;

    public ReviewCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText(Script1,
        [
            Unit(Script1, 1, TranslationStatus.Untranslated),
            Unit(Script1, 2, TranslationStatus.Machine),
            Unit(Script1, 3, TranslationStatus.Reviewed),
            Unit(Script1, 4, TranslationStatus.Machine),
            Unit(Script1, 5, TranslationStatus.Translated),
        ]);
        _project.SaveText("yst00002", [Unit("yst00002", 1, TranslationStatus.Machine)]);
        _project.SaveText("abc", [Unit("abc", 1, TranslationStatus.Machine)]);
        _cli.Cli.ReviewProviderFactory = (model, _) =>
        {
            _requestedModel = model;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status)
        {
            Origin = status == TranslationStatus.Machine ? new TranslationOrigin("anthropic", ClaudeModels.Opus, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)) : null,
        };

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["review", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private TextUnit Line(string key, int index) => _project.LoadText(key)[index];

    private string AllTextJson() =>
        string.Concat(_project.LoadAllText().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => TextUnitJson.SerializeList(p.Value)));

    [Fact]
    public async Task Linha_recusada_fica_sem_nota_e_aparece_no_resultado()
    {
        _cli.Cli.ReviewProviderFactory = (_, _) => new RefusingProvider();

        var result = await Run(FilesFlag, "abc", JsonFlag);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, Line("abc", 0).Notes);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("abc:000001", doc.RootElement.GetProperty("data").GetProperty("refused")[0].GetString());
    }

    private sealed class FailingProvider : IReviewProvider
    {
        public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new ArgumentException("Falha simulada.");
    }

    private sealed class RefusingProvider : IReviewProvider
    {
        public Task<IReadOnlyList<ReviewResult>> ReviewAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReviewResult>>([.. batch.Lines.Select(l => new ReviewResult(l.Id, string.Empty, Refused: true))]);
    }
}
