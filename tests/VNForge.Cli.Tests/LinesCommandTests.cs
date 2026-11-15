using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R2")]
public sealed class LinesCommandTests : IDisposable
{
    private const string JsonOption = "--json";
    private const string Yst00001 = "yst00001";
    private const string Yst00002 = "yst00002";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;
    private readonly ProjectFolder _project;

    public LinesCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        _project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Yst00001, [Unit(Yst00001, 1, "ＭＳ ゴシック"), Unit(Yst00001, 2, "原文2"), Unit(Yst00001, 3, "原文3", TranslationStatus.Approved, "ok")]);
        _project.SaveText(Yst00002, [Unit(Yst00002, 1, "原文4"), Unit(Yst00002, 2, "原文5")]);
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, string source, TranslationStatus status = TranslationStatus.Untranslated, string? translation = null) =>
        new($"{key}:{n:D6}", TextUnitKind.System, null, source, translation, status);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync(["lines", .. args, "--project", _projectPath], TestContext.Current.CancellationToken);

    private TextUnit Line(string key, int index) => _project.LoadText(key)[index];

    [Fact]
    public async Task Speaker_narracao_e_original_gravam_vazio_e_limpam_a_escolha()
    {
        _project.SaveText(Yst00002, [.. _project.LoadText(Yst00002).Select(u => u with { Speaker = "美咲" })]);
        await Run("speaker", Yst00002, "--nome", "Inui");

        var cleared = await Run("speaker", "yst00002:000001", "--original", JsonOption);
        var narration = await Run("speaker", "yst00002:000002", "--narracao");

        Assert.Equal(0, cleared.ExitCode);
        Assert.Null(Line(Yst00002, 0).ChosenSpeaker);
        Assert.Equal(string.Empty, Line(Yst00002, 1).ChosenSpeaker);
        Assert.Contains("Narração", narration.Output, StringComparison.Ordinal);
    }
}
