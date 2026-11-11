using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class LogLocationTests : IDisposable
{
    private const string Cache = ".cache";
    private const string Log = "*.log";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private void AddBug() => _cli.AddCommand("bug", _ => throw new InvalidOperationException("estado quebrado"));

    [Fact]
    public async Task Project_sem_vnforge_json_nao_cria_cache_na_pasta_e_loga_na_pasta_do_usuario()
    {
        var gameFolder = Path.Combine(_cli.Root, "pasta-do-jogo");
        Directory.CreateDirectory(gameFolder);
        AddBug();

        var result = await _cli.RunAsync(["--project", gameFolder, "bug"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(Directory.GetFileSystemEntries(gameFolder));
        Assert.Single(Directory.GetFiles(_cli.UserLogDirectory, Log));
    }

}
