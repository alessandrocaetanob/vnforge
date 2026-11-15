using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class TranslateCommandTests : IDisposable
{
    private const string Yst00001 = "yst00001";
    private const string Yst00002 = "yst00002";
    private const string Antiga = "antiga";
    private const string PT1 = "[PT] 原文1";
    private const string FilesOption = "--files";
    private const string JsonOption = "--json";
    private const string Grande = "grande";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly FakeTranslationProvider _provider = new();
    private string? _requestedModel;
    private ClaudeEffort? _requestedEffort;

    public TranslateCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Yst00001,
        [
            Unit(Yst00001, 1, TranslationStatus.Untranslated),
            Unit(Yst00001, 2, TranslationStatus.Machine),
            Unit(Yst00001, 3, TranslationStatus.Reviewed),
            Unit(Yst00001, 4, TranslationStatus.Untranslated),
        ]);
        _project.SaveText(Yst00002, [Unit(Yst00002, 1, TranslationStatus.Untranslated)]);
        _project.SaveText("abc", [Unit("abc", 1, TranslationStatus.Untranslated)]);
        _cli.Cli.TranslationProviderFactory = (model, effort) =>
        {
            _requestedModel = model;
            _requestedEffort = effort;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : Antiga, status);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["translate", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private TextUnit Line(string key, int index) => _project.LoadText(key)[index];

    [Fact]
    public async Task Dry_run_estima_sem_chamar_o_provedor_nem_gravar_nada()
    {
        var before = await File.ReadAllTextAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken);

        var result = await Run("--dry-run", JsonOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(_provider.ReceivedBatches);
        Assert.Equal(before, await File.ReadAllTextAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken));
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal(4, data.GetProperty("estimate").GetProperty("lines").GetInt32());
        Assert.True(data.GetProperty("estimate").GetProperty("inputTokens").GetInt64() > 0);
        Assert.True(data.GetProperty("estimate").GetProperty("dollars").GetDecimal() > 0);
    }

    [Fact]
    public async Task Sem_chave_de_api_avisa_em_portugues_e_sai_com_1()
    {
        Assert.SkipWhen(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")), "ANTHROPIC_API_KEY definida nesta máquina.");
        _cli.Cli.TranslationProviderFactory = null;

        var result = await Run();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("ANTHROPIC_API_KEY", result.Error);
        Assert.Null(Line("abc", 0).Translation);
    }

    /// <summary>Lança <see cref="ArgumentException"/>, que o runner não repete, para o teste não esperar o backoff.</summary>
    private sealed class FailingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            throw new ArgumentException("Falha simulada.");
    }

    private sealed class RefusingProvider : ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>([.. batch.Lines.Select(l => new TranslationResult(l.Id, string.Empty, Refused: true))]);
    }
}
