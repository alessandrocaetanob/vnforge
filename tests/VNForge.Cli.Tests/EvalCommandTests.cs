using System.Text.Json;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Eval;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class EvalCommandTests : IDisposable
{
    private const string Key = "yst00001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly List<string> _models = [];
    private readonly FakeJudgeProvider _judge = new(unit => unit.Translation!.StartsWith("Tradução", StringComparison.Ordinal) ? 9 : 4);

    public EvalCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText(Key, [.. Enumerable.Range(1, 60).Select(Unit)]);
        _cli.Cli.TranslationProviderFactory = (model, _) =>
        {
            _models.Add(model);
            return model == ClaudeModels.Haiku ? new FakeTranslationProvider(u => $"[PT] {u.Source}") : new FakeTranslationProvider(u => $"Tradução {u.Source[2..]}");
        };
        _cli.Cli.JudgeProviderFactory = (_, _) => _judge;
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(int i) => i % 3 == 0
        ? new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", null, TranslationStatus.Untranslated)
        : new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", $"Tradução {i}", TranslationStatus.Approved);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["eval", "--project", _projectPath, .. args], TestContext.Current.CancellationToken);

    private string TextJson => File.ReadAllText(_project.TextPath(Key));

    private void WriteAppJson(string json)
    {
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), json);
    }

    [Fact]
    public async Task Recusa_a_rodada_cuja_estimativa_passa_do_que_resta_do_limite_e_nao_gasta_nada()
    {
        WriteAppJson("""{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 10 }""");
        AiUsageLog.TryRecord(_project, new AiUsageTotals(0, 0, 0, 0, 1.99m, 1));

        var result = await Run("--config", "opus:high:30", "--config", "opus:high:30", "--config", "opus:high:30", "--sample", "40", "--no-judge");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("só restam", result.Error + result.Output);
        Assert.Contains("limite de gasto", result.Error + result.Output);
        Assert.Empty(_models);
        Assert.Null(EvalStore.LoadLatest(_project));
    }
}
