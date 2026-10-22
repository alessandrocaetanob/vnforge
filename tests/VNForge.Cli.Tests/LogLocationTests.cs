using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

public sealed class LogLocationTests : IDisposable
{
    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private void AddBug() => _cli.AddCommand("bug", _ => throw new InvalidOperationException("estado quebrado"));

    [Fact]
    public async Task Sem_project_e_fora_de_projeto_o_log_vai_para_a_pasta_do_usuario_e_nao_suja_o_cwd()
    {
        AddBug();

        await _cli.RunAsync(["bug"], TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(_cli.WorkingDirectory, ".cache")));
        var log = Assert.Single(Directory.GetFiles(_cli.UserLogDirectory, "*.log"));
        Assert.Contains("estado quebrado", File.ReadAllText(log));
    }

    [Fact]
    public async Task Cwd_com_vnforge_json_recebe_o_log_em_cache_logs()
    {
        File.WriteAllText(Path.Combine(_cli.WorkingDirectory, ProjectFolder.ConfigFileName), "{}");
        AddBug();

        await _cli.RunAsync(["bug"], TestContext.Current.CancellationToken);

        var logs = Path.Combine(_cli.WorkingDirectory, ".cache", "logs");
        Assert.Single(Directory.GetFiles(logs, "*.log"));
        Assert.False(Directory.Exists(_cli.UserLogDirectory));
    }

    [Fact]
    public async Task Project_leva_o_log_para_dentro_da_pasta_indicada()
    {
        var project = Path.Combine(_cli.Root, "projeto");
        Directory.CreateDirectory(project);
        AddBug();

        await _cli.RunAsync(["--project", project, "bug"], TestContext.Current.CancellationToken);

        Assert.Single(Directory.GetFiles(Path.Combine(project, ".cache", "logs"), "*.log"));
        Assert.False(Directory.Exists(Path.Combine(_cli.WorkingDirectory, ".cache")));
    }

    [Fact]
    public async Task Project_inexistente_e_erro_e_a_pasta_nao_e_criada()
    {
        var missing = Path.Combine(_cli.Root, "nao-existe");
        _cli.AddCommand("ok", _ => 0);

        var result = await _cli.RunAsync(["--project", missing, "ok"], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A pasta do projeto não existe", result.Error);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task Quiet_deixa_so_avisos_e_erros_no_log_e_verbose_inclui_o_debug()
    {
        _cli.AddCommand("ok", _ => 0);

        await _cli.RunAsync(["ok", "--verbose"], TestContext.Current.CancellationToken);
        var verboseLog = File.ReadAllText(Assert.Single(Directory.GetFiles(_cli.UserLogDirectory, "*.log")));
        Assert.Contains("Executando: ok --verbose", verboseLog);

        Directory.Delete(_cli.UserLogDirectory, recursive: true);
        await _cli.RunAsync(["ok", "--quiet"], TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(_cli.UserLogDirectory));
    }
}
