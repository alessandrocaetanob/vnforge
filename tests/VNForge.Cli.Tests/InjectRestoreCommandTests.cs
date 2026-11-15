using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Patching;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R1")]
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

    private void PutBuild(string content = Traduzido, string file = YsbinYpf) =>
        File.WriteAllText(Path.Combine(_project.BuildDirectory, file), content);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _project.Root], TestContext.Current.CancellationToken);

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
    public async Task Inject_num_disco_que_zera_o_rename_no_segundo_arquivo_falha_e_o_jogo_volta_como_estava()
    {
        const string Segundo = "zz.ypf";
        await File.WriteAllTextAsync(Path.Combine(_game, Segundo), Original, TestContext.Current.CancellationToken);
        PutBuild();
        PutBuild(Traduzido, Segundo);
        using var disco = ProjectDisk.Use(new ZeroingDisk(destino => destino.EndsWith(Segundo, StringComparison.OrdinalIgnoreCase) && destino.StartsWith(_game, StringComparison.OrdinalIgnoreCase)));

        var result = await Run(Inject, YesOption);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("depois de gravado", result.Error);
        Assert.Equal(Original, await File.ReadAllTextAsync(GameFile, TestContext.Current.CancellationToken));
        Assert.Equal(Original, await File.ReadAllTextAsync(Path.Combine(_game, Segundo), TestContext.Current.CancellationToken));
    }
}
