using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class ExportImportCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;

    public ExportImportCommandTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_cli.WorkingDirectory, "vn"), new ProjectConfig(
            "VN",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText("yst00001",
        [
            new("yst00001:000001", TextUnitKind.Dialogue, "美咲", "おはよう", null, TranslationStatus.Untranslated),
            new("yst00001:000002", TextUnitKind.Narration, null, "雨", "Chuva", TranslationStatus.Machine),
        ]);
    }

    public void Dispose() => _cli.Dispose();

    private string ProjectPath => _project.Root;

    [Fact]
    public async Task Export_grava_revisao_csv_na_pasta_do_projeto_e_import_sem_edicao_nao_muda_nada()
    {
        var antes = File.ReadAllBytes(_project.TextPath("yst00001"));

        var export = await _cli.RunAsync(["export", "--format", "csv", "--project", ProjectPath], TestContext.Current.CancellationToken);
        var import = await _cli.RunAsync(["import", Path.Combine(ProjectPath, "revisao.csv"), "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, export.ExitCode);
        Assert.Contains("Exportadas 2 linhas", export.Output);
        Assert.Equal(0, import.ExitCode);
        Assert.Contains("Nenhuma alteração", import.Output);
        Assert.True(antes.AsSpan().SequenceEqual(File.ReadAllBytes(_project.TextPath("yst00001"))));
    }

    [Fact]
    public async Task Import_aplica_a_edicao_do_revisor()
    {
        var csv = Path.Combine(ProjectPath, "revisao.csv");
        await _cli.RunAsync(["export", "--project", ProjectPath], TestContext.Current.CancellationToken);
        File.WriteAllText(csv, File.ReadAllText(csv).Replace("yst00001:000001,dialogue,美咲,おはよう,,untranslated,", "yst00001:000001,dialogue,美咲,おはよう,Bom dia,reviewed,ok"));

        var import = await _cli.RunAsync(["import", csv, "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, import.ExitCode);
        Assert.Contains("Importadas 1 linha alterada em 1 script", import.Output);
        Assert.Equal("Bom dia", _project.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Import_com_id_desconhecido_sai_com_erro_e_nao_altera_o_projeto()
    {
        var csv = Path.Combine(ProjectPath, "revisao.csv");
        await _cli.RunAsync(["export", "--project", ProjectPath], TestContext.Current.CancellationToken);
        var antes = File.ReadAllBytes(_project.TextPath("yst00001"));
        File.WriteAllText(csv, File.ReadAllText(csv)
            .Replace("yst00001:000001,dialogue,美咲,おはよう,,untranslated,", "yst00001:000001,dialogue,美咲,おはよう,Bom dia,reviewed,")
            .Replace("yst00001:000002", "yst00001:777777"));

        var import = await _cli.RunAsync(["import", csv, "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, import.ExitCode);
        Assert.Contains("yst00001:777777", import.Error);
        Assert.True(antes.AsSpan().SequenceEqual(File.ReadAllBytes(_project.TextPath("yst00001"))));
    }

    [Fact]
    public async Task Export_nao_troca_arquivo_existente_sem_force()
    {
        await _cli.RunAsync(["export", "--project", ProjectPath], TestContext.Current.CancellationToken);

        var again = await _cli.RunAsync(["export", "--project", ProjectPath], TestContext.Current.CancellationToken);
        var forced = await _cli.RunAsync(["export", "--force", "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, again.ExitCode);
        Assert.Contains("--force", again.Error);
        Assert.Equal(0, forced.ExitCode);
    }

    [Fact]
    public async Task Formato_desconhecido_e_erro_de_uso()
    {
        var result = await _cli.RunAsync(["export", "--format", "xlsx", "--project", ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("xlsx", result.Error);
    }
}
