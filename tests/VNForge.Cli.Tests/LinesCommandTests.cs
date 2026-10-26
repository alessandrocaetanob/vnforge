using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

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
    public async Task Keep_marca_as_linhas_pelos_ids_e_o_json_conta_o_que_mudou()
    {
        var result = await Run("keep", "yst00001:000001,yst00002:000002", JsonOption);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("keep", data.GetProperty("action").GetString());
        Assert.Equal(2, data.GetProperty("changed").GetInt32());
        Assert.Equal(0, data.GetProperty("unchanged").GetInt32());
        Assert.Equal(TranslationStatus.Keep, Line(Yst00001, 0).Status);
        Assert.Equal(TranslationStatus.Keep, Line(Yst00002, 1).Status);
        Assert.Equal(TranslationStatus.Untranslated, Line(Yst00001, 1).Status);
    }

    [Fact]
    public async Task Chave_de_script_marca_todas_as_linhas_dele()
    {
        var result = await Run("keep", Yst00002);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2 linhas", result.Output, StringComparison.Ordinal);
        Assert.All(_project.LoadText(Yst00002), u => Assert.Equal(TranslationStatus.Keep, u.Status));
        Assert.Equal(TranslationStatus.Untranslated, Line(Yst00001, 0).Status);
    }

    [Fact]
    public async Task Keep_numa_linha_traduzida_apaga_a_traducao_e_repetir_nao_muda_nada()
    {
        await Run("keep", "yst00001:000003");
        var again = await Run("keep", "yst00001:000003", JsonOption);

        var line = Line(Yst00001, 2);
        Assert.Equal(TranslationStatus.Keep, line.Status);
        Assert.Null(line.Translation);
        using var doc = JsonDocument.Parse(again.Output);
        Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("changed").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task Unkeep_devolve_so_as_linhas_keep_a_sem_traducao()
    {
        await Run("keep", Yst00001);

        var result = await Run("unkeep", "yst00001:000001,yst00001:000002", JsonOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TranslationStatus.Untranslated, Line(Yst00001, 0).Status);
        Assert.Equal(TranslationStatus.Untranslated, Line(Yst00001, 1).Status);
        Assert.Equal(TranslationStatus.Keep, Line(Yst00001, 2).Status);
    }

    [Fact]
    public async Task Alvo_desconhecido_sai_com_1_e_nao_grava_nada()
    {
        var result = await Run("keep", "yst00001:000001,yst00001:009999");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("yst00001:009999", result.Error, StringComparison.Ordinal);
        Assert.Equal(TranslationStatus.Untranslated, Line(Yst00001, 0).Status);
    }

    [Fact]
    public async Task Status_conta_as_linhas_keep_como_prontas()
    {
        await Run("keep", Yst00002);

        var result = await _cli.RunAsync(["status", "--project", _projectPath, JsonOption], TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(result.Output);
        var files = doc.RootElement.GetProperty("data").GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!);
        Assert.Equal(2, files[Yst00002].GetProperty("keep").GetInt32());
        Assert.Equal(100, files[Yst00002].GetProperty("percent").GetDouble());
    }
}
