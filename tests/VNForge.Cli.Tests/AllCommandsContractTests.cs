using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VNForge.Cli.Tests;

/// <summary>Percorre a árvore real de comandos: ajuda em português e <c>--json</c> no formato comum (E9.2).</summary>
public sealed partial class AllCommandsContractTests : IDisposable
{
    private static readonly string[] ErrorKinds = ["expected", "unexpected", "cancelled"];

    private static readonly string[] EnglishMarkers =
    [
        "Description:", "Usage:", "Options:", "Commands:", "Arguments:", "[options]", "Show help", "Show version",
    ];

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    public static TheoryData<string> CommandPaths()
    {
        using var harness = new CliHarness();
        var data = new TheoryData<string>();
        foreach (var path in Walk(harness.Cli.Root, []))
        {
            data.Add(string.Join(' ', path));
        }

        return data;
    }

    [Fact]
    public void Arvore_de_comandos_nao_esta_vazia()
    {
        Assert.True(CommandPaths().Count > 10);
    }

    [Theory]
    [MemberData(nameof(CommandPaths))]
    public void Descricoes_de_comando_argumentos_e_opcoes_existem(string path)
    {
        var command = Find(path);

        Assert.False(string.IsNullOrWhiteSpace(command.Description), $"'{path}' sem descrição.");
        foreach (var argument in command.Arguments)
        {
            Assert.False(string.IsNullOrWhiteSpace(argument.Description), $"'{path}': argumento <{argument.Name}> sem descrição.");
        }

        foreach (var option in command.Options.Where(o => !o.Hidden))
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Description), $"'{path}': opção {option.Name} sem descrição.");
        }
    }

    [Theory]
    [MemberData(nameof(CommandPaths))]
    public async Task Ajuda_esta_em_portugues(string path)
    {
        var args = path.Length == 0 ? new[] { "--help" } : [.. path.Split(' '), "--help"];

        var result = await _cli.RunAsync(args, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Uso:", result.Output);
        Assert.Contains("--json", result.Output);
        Assert.Contains("--yes", result.Output);
        foreach (var marker in EnglishMarkers)
        {
            Assert.DoesNotContain(marker, result.Output);
        }

        Assert.DoesNotMatch(EnglishDescription(), result.Output);
    }

    [Theory]
    [MemberData(nameof(CommandPaths))]
    public async Task Json_sempre_devolve_um_envelope_em_uma_linha(string path)
    {
        if (path.Length == 0)
        {
            return; // `vnf` sem comando mostra a ajuda; --version --json é coberto em RootCommandTests.
        }

        // Sem projeto e sem argumentos: cada comando precisa falhar de forma controlada e no formato comum.
        var result = await _cli.RunAsync([.. path.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--json", "--yes"], TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.Error);
        var line = Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var ok = root.GetProperty("ok").GetBoolean();
        Assert.Equal(result.ExitCode, root.GetProperty("exitCode").GetInt32());
        if (ok)
        {
            Assert.False(root.TryGetProperty("error", out _));
        }
        else
        {
            var error = root.GetProperty("error");
            Assert.Contains(error.GetProperty("kind").GetString(), ErrorKinds);
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            Assert.DoesNotContain("   at ", line);
        }
    }

    [Fact]
    public async Task Json_de_sucesso_segue_o_formato_comum()
    {
        var result = await _cli.RunAsync(["config", "ai", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var line = Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        using var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    private Command Find(string path)
    {
        Command current = _cli.Cli.Root;
        foreach (var name in path.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Subcommands.Single(c => c.Name == name);
        }

        return current;
    }

    private static IEnumerable<string[]> Walk(Command command, string[] path)
    {
        yield return path;
        foreach (var sub in command.Subcommands)
        {
            foreach (var inner in Walk(sub, [.. path, sub.Name]))
            {
                yield return inner;
            }
        }
    }

    // Linhas de descrição que começam com verbo inglês comum denunciam texto sem tradução.
    [GeneratedRegex(@"^\s{2}\S.*\s{2}(Show|Display|Print|Create|Delete|Run|Set|Get|Lists?|Reads?|Writes?|Creates|Shows|Gets)\b", RegexOptions.Multiline)]
    private static partial Regex EnglishDescription();
}
