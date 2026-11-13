using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class GameCommandTests : IDisposable
{
    private const string YsbinYpf = "ysbin.ypf";

    private readonly CliHarness _cli = new();
    private readonly string _oldGame;
    private readonly string _newGame;
    private readonly ProjectFolder _project;

    public GameCommandTests()
    {
        _oldGame = Path.Combine(_cli.Root, "antigo");
        _newGame = Path.Combine(_cli.Root, "novo");
        Directory.CreateDirectory(_newGame);
        File.WriteAllText(Path.Combine(_newGame, YsbinYpf), "original");
        _project = ProjectFolder.Create(
            Path.Combine(_cli.Root, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_oldGame, "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
        _project.SaveState(new PipelineState([new OriginalFile(YsbinYpf, 8, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("original"))))], new Dictionary<string, StageState>(), _oldGame));
    }

    public void Dispose() => _cli.Dispose();

    private Task<RunResult> Run(params string[] args) =>
        _cli.RunAsync([.. args, "--project", _project.Root], TestContext.Current.CancellationToken);

    [Fact]
    public async Task Move_aponta_o_projeto_para_a_pasta_nova_e_recusa_a_pasta_de_outro_jogo_sem_gravar()
    {
        var other = Path.Combine(_cli.Root, "outro");
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(other, YsbinYpf), "outro jogo", TestContext.Current.CancellationToken);

        var refused = await Run("game", "move", "--game", other);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains(YsbinYpf, refused.Error);
        Assert.Equal(_oldGame, _project.LoadState().GameDirectory);

        var moved = await Run("game", "move", "--game", _newGame);

        Assert.Equal(0, moved.ExitCode);
        Assert.Contains("Jogo movido", moved.Output);
        Assert.Equal(_newGame, _project.LoadState().GameDirectory);
        Assert.Equal(_newGame.Replace('\\', '/'), _project.LoadConfig().Game.Path);

        var again = await Run("game", "move", "--game", _newGame);
        var json = await Run("game", "move", "--game", _newGame, "--json");

        Assert.Contains("Nada foi gravado", again.Output);
        Assert.Contains("checkedFiles", json.Output);
    }
}
