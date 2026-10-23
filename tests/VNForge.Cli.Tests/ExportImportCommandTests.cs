using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class ExportImportCommandTests : IDisposable
{
    private const string Yst00001 = "yst00001";
    private const string Export = "export";
    private const string ProjectOption = "--project";

    private readonly CliHarness _cli = new();
    private readonly ProjectFolder _project;

    public ExportImportCommandTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_cli.WorkingDirectory, "vn"), new ProjectConfig(
            "VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Yst00001,
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
        var antes = await File.ReadAllBytesAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken);

        var export = await _cli.RunAsync([Export, "--format", "csv", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        var import = await _cli.RunAsync(["import", Path.Combine(ProjectPath, "revisao.csv"), ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, export.ExitCode);
        Assert.Contains("Exportadas 2 linhas", export.Output);
        Assert.Equal(0, import.ExitCode);
        Assert.Contains("Nenhuma alteração", import.Output);
        var depois = await File.ReadAllBytesAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken);
        Assert.True(antes.AsSpan().SequenceEqual(depois));
    }

    [Fact]
    public async Task Import_aplica_a_edicao_do_revisor()
    {
        var csv = Path.Combine(ProjectPath, "revisao.csv");
        await _cli.RunAsync([Export, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(csv, (await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken)).Replace("yst00001:000001,dialogue,美咲,おはよう,,untranslated,", "yst00001:000001,dialogue,美咲,おはよう,Bom dia,reviewed,ok"), TestContext.Current.CancellationToken);

        var import = await _cli.RunAsync(["import", csv, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, import.ExitCode);
        Assert.Contains("Importadas 1 linha alterada em 1 script", import.Output);
        Assert.Equal("Bom dia", _project.LoadText(Yst00001)[0].Translation);
    }

    [Fact]
    public async Task Import_com_id_desconhecido_sai_com_erro_e_nao_altera_o_projeto()
    {
        var csv = Path.Combine(ProjectPath, "revisao.csv");
        await _cli.RunAsync([Export, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        var antes = await File.ReadAllBytesAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(csv, (await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken))
            .Replace("yst00001:000001,dialogue,美咲,おはよう,,untranslated,", "yst00001:000001,dialogue,美咲,おはよう,Bom dia,reviewed,")
            .Replace("yst00001:000002", "yst00001:777777"), TestContext.Current.CancellationToken);

        var import = await _cli.RunAsync(["import", csv, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, import.ExitCode);
        Assert.Contains("yst00001:777777", import.Error);
        var depois = await File.ReadAllBytesAsync(_project.TextPath(Yst00001), TestContext.Current.CancellationToken);
        Assert.True(antes.AsSpan().SequenceEqual(depois));
    }

    [Fact]
    public async Task Export_nao_troca_arquivo_existente_sem_force()
    {
        await _cli.RunAsync([Export, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        var again = await _cli.RunAsync([Export, ProjectOption, ProjectPath], TestContext.Current.CancellationToken);
        var forced = await _cli.RunAsync([Export, "--force", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, again.ExitCode);
        Assert.Contains("--force", again.Error);
        Assert.Equal(0, forced.ExitCode);
    }

    [Fact]
    public async Task Formato_desconhecido_e_erro_de_uso()
    {
        var result = await _cli.RunAsync([Export, "--format", "xlsx", ProjectOption, ProjectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("xlsx", result.Error);
    }
}
