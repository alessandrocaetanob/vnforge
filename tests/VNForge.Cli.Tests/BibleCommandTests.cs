using System.Text.Json;
using VNForge.AI.Bible;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.Core.Bible;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R5")]
public sealed class BibleCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;
    private readonly FakeBibleProvider _provider = new();
    private string? _requestedModel;

    public BibleCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = AiTestProject.Create(_cli.Root, _projectPath);
        _project.SaveText("yst1", [Line("yst1", 1, "おはよう"), Line("yst1", 2, "遅いぞ")]);
        _project.SaveText("yst2", [Line("yst2", 1, "ただいま")]);
        _project.SaveText("yst3", [Line("yst3", 1, "さようなら")]);
        _project.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
        {
            ["yst1"] = new("manha", "userscript/01_cap1"),
            ["yst2"] = new("tarde", "userscript/01_cap1"),
            ["yst3"] = new("noite", "userscript/02_cap2"),
        }).WithGroup("yst1", "Manhã de chuva").WithGroup("yst2", "Manhã de chuva").WithGroup("yst3", "Festival"));
        _cli.Cli.BibleProviderFactory = (model, _) =>
        {
            _requestedModel = model;
            return _provider;
        };
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Line(string key, int n, string source) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", source, null, TranslationStatus.Untranslated);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["bible", .. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Estimativa_maior_que_o_restante_do_limite_recusa_antes_de_gastar()
    {
        Directory.CreateDirectory(_cli.UserConfigDirectory);
        File.WriteAllText(Path.Combine(_cli.UserConfigDirectory, AppBudgetFile.FileName), """{ "exchangeRate": 5, "exchangeRateOn": "2026-10-25", "spendLimit": 0.01 }""");
        _cli.SetInput("s\n");

        var result = await Run("generate");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("passaria do limite", result.Output, StringComparison.Ordinal);
        Assert.Empty(_provider.SceneCalls);
        Assert.False(File.Exists(_project.BiblePath));
    }
}
