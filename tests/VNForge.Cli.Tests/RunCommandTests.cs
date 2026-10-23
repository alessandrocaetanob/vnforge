using System.Text.Json;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class RunCommandTests : IDisposable
{
    private const string FakeContent = "sintetico";
    private const string ExeName = "jogo.exe";

    private readonly CliHarness _cli = new();
    private readonly FakeStarter _starter = new();
    private readonly string _game;
    private readonly ProjectFolder _project;

    public RunCommandTests()
    {
        _cli.Cli.GameProcessStarter = _starter;
        _game = Path.Combine(_cli.Root, "jogo");
        Directory.CreateDirectory(_game);
        _project = ProjectFolder.Create(
            Path.Combine(_cli.Root, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game, ExeName),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose() => _cli.Dispose();

    private string Exe => Path.Combine(_game, ExeName);

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _project.Root], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Run_abre_o_executavel_na_pasta_do_jogo()
    {
        await File.WriteAllTextAsync(Exe, FakeContent, TestContext.Current.CancellationToken);

        var result = await Run("run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"Jogo aberto: {Exe}", result.Output);
        Assert.Equal((Exe, _game), Assert.Single(_starter.Started));
    }

    [Fact]
    public async Task Run_com_json_devolve_o_caminho_do_executavel()
    {
        await File.WriteAllTextAsync(Exe, FakeContent, TestContext.Current.CancellationToken);

        var result = await Run("run", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(Exe, document.RootElement.GetProperty("data").GetProperty("executable").GetString());
    }

    [Fact]
    public async Task Run_sem_o_executavel_falha_mostrando_o_caminho_procurado_e_nao_abre_nada()
    {
        var result = await Run("run");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(Exe, result.Error);
        Assert.Empty(_starter.Started);
    }

    [Fact]
    public async Task Run_resolve_game_path_relativo_a_pasta_do_projeto()
    {
        var relative = Path.Combine("..", "jogo-relativo");
        var folder = Path.GetFullPath(relative, _project.Root);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, ExeName), FakeContent, TestContext.Current.CancellationToken);
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(relative, ExeName) });

        var result = await Run("run");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Path.Combine(folder, ExeName), Assert.Single(_starter.Started).Executable);
    }

    [Fact]
    public async Task Run_com_exe_em_subpasta_usa_a_pasta_do_jogo_como_diretorio_de_trabalho()
    {
        Directory.CreateDirectory(Path.Combine(_game, "bin"));
        await File.WriteAllTextAsync(Path.Combine(_game, "bin", "vn.exe"), FakeContent, TestContext.Current.CancellationToken);
        _project.SaveConfig(_project.LoadConfig() with { Game = new GameConfig(_game, Path.Combine("bin", "vn.exe")) });

        var result = await Run("run");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal((Path.Combine(_game, "bin", "vn.exe"), _game), Assert.Single(_starter.Started));
    }

    private sealed class FakeStarter : IGameProcessStarter
    {
        public List<(string Executable, string WorkingDirectory)> Started { get; } = [];

        public void Start(string executablePath, string workingDirectory) => Started.Add((executablePath, workingDirectory));
    }
}
