using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class StatusCommandTests : IDisposable
{
    private const string Yst00001 = "yst00001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;

    public StatusCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        var project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        project.SaveText(Yst00001,
        [
            Unit(Yst00001, 1, "美咲", TranslationStatus.Approved),
            Unit(Yst00001, 2, "美咲", TranslationStatus.Untranslated),
            Unit(Yst00001, 3, null, TranslationStatus.Machine),
        ]);
        project.SaveText("yst00002", [Unit("yst00002", 1, "健", TranslationStatus.Reviewed)]);
    }

    public void Dispose() => _cli.Dispose();

    private static TextUnit Unit(string key, int n, string? speaker, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, speaker, "原文", status == TranslationStatus.Untranslated ? null : "tradução", status);

    [Fact]
    public async Task Json_traz_total_arquivos_e_personagens_que_batem_com_os_text_json()
    {
        var result = await _cli.RunAsync(["status", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var data = doc.RootElement.GetProperty("data");

        var total = data.GetProperty("total");
        Assert.Equal(4, total.GetProperty("lines").GetInt32());
        Assert.Equal(1, total.GetProperty("untranslated").GetInt32());
        Assert.Equal(1, total.GetProperty("machine").GetInt32());
        Assert.Equal(0, total.GetProperty("translated").GetInt32());
        Assert.Equal(1, total.GetProperty("reviewed").GetInt32());
        Assert.Equal(1, total.GetProperty("approved").GetInt32());
        Assert.Equal(75, total.GetProperty("percent").GetDouble());

        var files = data.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal([Yst00001, "yst00002"], files.Select(f => f.GetProperty("key").GetString()));
        Assert.Equal([3, 1], files.Select(f => f.GetProperty("lines").GetInt32()));

        var characters = data.GetProperty("characters").EnumerateArray().ToList();
        Assert.Equal(["美咲", "健", null], characters.Select(c => c.GetProperty("speaker").GetString()));
        Assert.Equal([2, 1, 1], characters.Select(c => c.GetProperty("lines").GetInt32()));
    }

    [Fact]
    public async Task Texto_mostra_as_tres_secoes_em_portugues()
    {
        var result = await _cli.RunAsync(["status", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("4 linhas", result.Output);
        Assert.Contains("Por arquivo", result.Output);
        Assert.Contains(Yst00001, result.Output);
        Assert.Contains("Por personagem", result.Output);
        Assert.Contains("美咲", result.Output);
        Assert.Contains("(sem falante)", result.Output);
    }

    [Fact]
    public async Task Pasta_sem_projeto_sai_com_1()
    {
        var result = await _cli.RunAsync(["status", "--project", _cli.WorkingDirectory], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
    }
}
