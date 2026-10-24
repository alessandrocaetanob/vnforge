using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class InjectRestoreCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";
    private const string Original = "original";
    private const string Traduzido = "traduzido";
    private const string Inject = "inject";
    private const string YesOption = "--yes";
    private const string Restore = "restore";

    private readonly CliHarness _cli = new();
    private readonly string _game;
    private readonly ProjectFolder _project;

    public InjectRestoreCommandTests()
    {
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, YsbinYpf), Original);
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
        _cli.Dispose();
    }

    private string GameFile => Path.Combine(_game, YsbinYpf);

    private void PutBuild(string content = Traduzido) =>
        File.WriteAllText(Path.Combine(_project.BuildDirectory, YsbinYpf), content);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _project.Root], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Inject_em_terminal_interativo_mostra_a_tabela_do_que_vai_mudar_antes_de_perguntar()
    {
        PutBuild();
        using var rich = new CliHarness(stdin: "n\n", terminal: TestTerminals.InteractivePlain(200));

        var result = await rich.RunAsync([Inject, "--project", _project.Root], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("O que acontece", result.Output);
        Assert.Contains(YsbinYpf, result.Output);
        Assert.Contains("substitui o original", result.Output);
        Assert.Contains("Gravar 1 arquivo(s)", result.Error);
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_em_terminal_interativo_mostra_a_tabela_do_que_volta_antes_de_perguntar()
    {
        PutBuild();
        await Run(Inject, YesOption);
        using var rich = new CliHarness(stdin: "n\n", terminal: TestTerminals.InteractivePlain(200));

        var result = await rich.RunAsync([Restore, "--project", _project.Root], TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("O que acontece", result.Output);
        Assert.Contains("volta ao original", result.Output);
        Assert.Contains("Restaurar 1 arquivo(s)", result.Error);
        Assert.Equal(Traduzido, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inject_e_restore_com_json_nao_desenham_tabela_mesmo_no_terminal_interativo()
    {
        PutBuild();
        using var rich = new CliHarness(terminal: TestTerminals.InteractivePlain(200));

        var inject = await rich.RunAsync([Inject, YesOption, "--json", "--project", _project.Root], TestContext.Current.CancellationToken);
        var restore = await rich.RunAsync([Restore, YesOption, "--json", "--project", _project.Root], TestContext.Current.CancellationToken);

        Assert.DoesNotContain("O que acontece", inject.Output);
        Assert.DoesNotContain("O que acontece", restore.Output);
        using var document = JsonDocument.Parse(inject.Output);
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Inject_com_yes_faz_backup_e_grava_o_build()
    {
        PutBuild();

        var result = await Run(Inject, YesOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("Backup:", result.Output);
        Assert.Contains("Injetado: 1 arquivo(s)", result.Output);
        Assert.Equal(Traduzido, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
        var backup = Assert.Single(Directory.GetDirectories(_project.BackupDirectory));
        Assert.Equal(Original, await File.ReadAllTextAsync(Path.Combine(backup, YsbinYpf), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inject_com_json_devolve_o_envelope_com_backup_e_arquivos()
    {
        PutBuild();

        var result = await Run(Inject, YesOption, "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(_game, data.GetProperty("gameDirectory").GetString());
        Assert.StartsWith(_project.BackupDirectory, data.GetProperty("backupDirectory").GetString());
        Assert.Equal(YsbinYpf, data.GetProperty("written")[0].GetString());
        Assert.Equal(0, data.GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task Inject_sem_yes_pergunta_e_grava_quando_a_resposta_e_sim()
    {
        PutBuild();
        _cli.SetInput("s\n");

        var result = await Run(Inject);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Gravar 1 arquivo(s)", result.Error);
        Assert.Equal(Traduzido, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inject_sem_yes_e_resposta_nao_sai_com_3_sem_gravar_nada()
    {
        PutBuild();
        _cli.SetInput("n\n");

        var result = await Run(Inject);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Nada foi gravado", result.Output);
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Inject_sem_yes_e_sem_entrada_pede_o_yes()
    {
        PutBuild();
        _cli.SetInput(string.Empty);

        var result = await Run(Inject);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(YesOption, result.Error);
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inject_com_json_sem_yes_pede_o_yes()
    {
        PutBuild();

        var result = await Run(Inject, "--json");

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(YesOption, document.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inject_sem_build_explica_em_portugues()
    {
        var result = await Run(Inject, YesOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("build/ está vazia", result.Error);
        Assert.Contains("vnf build", result.Error);
    }

    [Fact]
    public async Task Inject_fora_de_um_projeto_explica_em_portugues()
    {
        var result = await _cli.RunAsync([Inject, YesOption], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vnforge.json não encontrado", result.Error);
    }

    [Fact]
    public async Task Inject_com_build_igual_ao_jogo_nao_faz_nada()
    {
        PutBuild(Original);

        var result = await Run(Inject);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Nada a injetar", result.Output);
        Assert.Empty(Directory.GetDirectories(_project.BackupDirectory));
    }

    [Fact]
    public async Task Restore_com_yes_devolve_o_original()
    {
        PutBuild();
        await Run(Inject, YesOption);

        var result = await Run(Restore, YesOption);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Restaurado: 1 arquivo(s)", result.Output);
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_com_json_lista_o_que_voltou()
    {
        PutBuild();
        await Run(Inject, YesOption);

        var result = await Run(Restore, YesOption, "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(YsbinYpf, data.GetProperty("restored")[0].GetString());
        Assert.Equal(0, data.GetProperty("removed").GetArrayLength());
    }

    [Fact]
    public async Task Restore_sem_yes_e_resposta_nao_nao_grava()
    {
        PutBuild();
        await Run(Inject, YesOption);
        _cli.SetInput("\n");

        var result = await Run(Restore);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(Traduzido, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_sem_backup_explica_em_portugues()
    {
        var result = await Run(Restore, YesOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Não há backup", result.Error);
    }
}
