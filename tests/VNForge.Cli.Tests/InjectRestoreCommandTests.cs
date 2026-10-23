using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class InjectRestoreCommandTests : IDisposable
{
    private readonly CliHarness _cli = new();
    private readonly TextReader _originalIn = Console.In;
    private readonly string _game;
    private readonly ProjectFolder _project;

    public InjectRestoreCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "ysbin.ypf"), "original");
        _project = ProjectFolder.Create(
            Path.Combine(_cli.Root, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose()
    {
        Console.SetIn(_originalIn);
        _cli.Dispose();
    }

    private string GameFile => Path.Combine(_game, "ysbin.ypf");

    private void PutBuild(string content = "traduzido") =>
        File.WriteAllText(Path.Combine(_project.BuildDirectory, "ysbin.ypf"), content);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _project.Root], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Inject_com_yes_faz_backup_e_grava_o_build()
    {
        PutBuild();

        var result = await Run("inject", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("Backup:", result.Output);
        Assert.Contains("Injetado: 1 arquivo(s)", result.Output);
        Assert.Equal("traduzido", File.ReadAllText(GameFile));
        var backup = Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal("original", File.ReadAllText(Path.Combine(backup, "ysbin.ypf")));
    }

    [Fact]
    public async Task Inject_com_json_devolve_o_envelope_com_backup_e_arquivos()
    {
        PutBuild();

        var result = await Run("inject", "--yes", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(_game, data.GetProperty("gameDirectory").GetString());
        Assert.StartsWith(_project.BackupDirectory, data.GetProperty("backupDirectory").GetString());
        Assert.Equal("ysbin.ypf", data.GetProperty("written")[0].GetString());
        Assert.Equal(0, data.GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task Inject_sem_yes_pergunta_e_grava_quando_a_resposta_e_sim()
    {
        PutBuild();
        Console.SetIn(new StringReader("s\n"));

        var result = await Run("inject");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Gravar 1 arquivo(s)", result.Error);
        Assert.Equal("traduzido", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Inject_sem_yes_e_resposta_nao_sai_com_3_sem_gravar_nada()
    {
        PutBuild();
        Console.SetIn(new StringReader("n\n"));

        var result = await Run("inject");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Nada foi gravado", result.Output);
        Assert.Equal("original", File.ReadAllText(GameFile));
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Inject_sem_yes_e_sem_entrada_pede_o_yes()
    {
        PutBuild();
        Console.SetIn(new StringReader(string.Empty));

        var result = await Run("inject");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--yes", result.Error);
        Assert.Equal("original", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Inject_com_json_sem_yes_pede_o_yes()
    {
        PutBuild();

        var result = await Run("inject", "--json");

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("--yes", document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal("original", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Inject_sem_build_explica_em_portugues()
    {
        var result = await Run("inject", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("build/ está vazia", result.Error);
        Assert.Contains("vnf build", result.Error);
    }

    [Fact]
    public async Task Inject_fora_de_um_projeto_explica_em_portugues()
    {
        var result = await _cli.RunAsync(["inject", "--yes"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnforge.json não encontrado", result.Error);
    }

    [Fact]
    public async Task Inject_com_build_igual_ao_jogo_nao_faz_nada()
    {
        PutBuild("original");

        var result = await Run("inject");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nada a injetar", result.Output);
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Restore_com_yes_devolve_o_original()
    {
        PutBuild();
        await Run("inject", "--yes");

        var result = await Run("restore", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Restaurado: 1 arquivo(s)", result.Output);
        Assert.Equal("original", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Restore_com_json_lista_o_que_voltou()
    {
        PutBuild();
        await Run("inject", "--yes");

        var result = await Run("restore", "--yes", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("ysbin.ypf", data.GetProperty("restored")[0].GetString());
        Assert.Equal(0, data.GetProperty("removed").GetArrayLength());
    }

    [Fact]
    public async Task Restore_sem_yes_e_resposta_nao_nao_grava()
    {
        PutBuild();
        await Run("inject", "--yes");
        Console.SetIn(new StringReader("\n"));

        var result = await Run("restore");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("traduzido", File.ReadAllText(GameFile));
    }

    [Fact]
    public async Task Restore_sem_backup_explica_em_portugues()
    {
        var result = await Run("restore", "--yes");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Não há backup", result.Error);
    }
}
