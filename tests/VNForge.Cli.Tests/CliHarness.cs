using System.CommandLine;
using VNForge.AI.Settings;
using VNForge.Cli;

namespace VNForge.Cli.Tests;

/// <summary>Roda o <see cref="VnfCli"/> em processo, com cwd e pasta de log próprios e saída capturada.</summary>
internal sealed class CliHarness : IDisposable
{
    public CliHarness(string? stdin = null, bool realVault = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "vnforge-cli-tests-" + Guid.NewGuid().ToString("N"));
        WorkingDirectory = Path.Combine(Root, "cwd");
        UserLogDirectory = Path.Combine(Root, "user-logs");
        Directory.CreateDirectory(WorkingDirectory);
        UserConfigDirectory = Path.Combine(Root, "user-config");
        Cli = VnfCli.Create(WorkingDirectory, UserLogDirectory, UserConfigDirectory, new StringReader(stdin ?? string.Empty),
            realVault ? null : new InMemoryApiKeyStore());
        // Os testes de comando não dependem das regras reais de QA (testadas em Core.Tests).
        Cli.Qa = new VNForge.Core.Qa.QaEngine([]);
    }

    public string Root { get; }

    public string WorkingDirectory { get; }

    public string UserLogDirectory { get; }

    public string UserConfigDirectory { get; }

    public VnfCli Cli { get; }

    public void AddCommand(string name, Func<ParseResult, CancellationToken, Task<int>> action)
    {
        var command = new Command(name, "Comando de teste.");
        command.SetAction(action);
        Cli.Root.Subcommands.Add(command);
    }

    public void AddCommand(string name, Func<ParseResult, int> action) =>
        AddCommand(name, (parseResult, _) => Task.FromResult(action(parseResult)));

    public async Task<RunResult> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await Cli.RunAsync(args, output, error, cancellationToken);
        return new RunResult(code, output.ToString(), error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private string? _key;

    public string? Load() => _key;

    public void Save(string apiKey) => _key = apiKey;

    public bool Delete()
    {
        var had = _key is not null;
        _key = null;
        return had;
    }
}

internal sealed record RunResult(int ExitCode, string Output, string Error);
