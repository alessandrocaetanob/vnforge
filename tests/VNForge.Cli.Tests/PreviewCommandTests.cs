using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class PreviewCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly string _projectPath;

    public PreviewCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        var project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        File.WriteAllText(project.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        project.SaveText("a001", [new("a001:000001", TextUnitKind.Dialogue, "美咲", "原文", "um dois tres quatro", TranslationStatus.Translated)]);
    }

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Texto_mostra_linhas_quebradas_colunas_e_avisos()
    {
        var result = await _cli.RunAsync(["preview", "a001:000001", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("um dois", result.Output);
        Assert.Contains("3 linhas", result.Output);
        Assert.Contains("7 colunas", result.Output);
        Assert.Contains("caixa 1 com 3 linhas (máximo 2)", result.Output);
    }

    [Fact]
    public async Task Json_traz_texto_caixas_colunas_e_avisos()
    {
        var result = await _cli.RunAsync(["preview", "a001:000001", "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("a001:000001", data.GetProperty("id").GetString());
        Assert.Equal("um dois\\ntres\\nquatro", data.GetProperty("text").GetString());
        Assert.Equal(3, data.GetProperty("lines").GetInt32());
        Assert.Equal(10, data.GetProperty("maxWidth").GetInt32());
        var box = Assert.Single(data.GetProperty("boxes").EnumerateArray());
        Assert.Equal([7, 4, 6], box.GetProperty("columns").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Single(data.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Id_inexistente_sai_com_1_e_mensagem_em_portugues()
    {
        var result = await _cli.RunAsync(["preview", "a001:999999", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Não existe linha com o id 'a001:999999'", result.Error);
    }
}
