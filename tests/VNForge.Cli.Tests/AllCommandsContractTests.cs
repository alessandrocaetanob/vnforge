using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VNForge.Cli.Tests;

/// <summary>Percorre a árvore real de comandos: ajuda em português e <c>--json</c> no formato comum (E9.2).</summary>
public sealed partial class AllCommandsContractTests
{
    private static readonly string[] ErrorKinds = ["expected", "unexpected", "cancelled"];

    private static readonly string[] EnglishMarkers =
    [
        "Description:", "Usage:", "Options:", "Commands:", "Arguments:", "[options]", "Show help", "Show version",
    ];

    private static string[] CommandPaths()
    {
        using var harness = new CliHarness();
        return [.. Walk(harness.Cli.Root, []).Select(path => string.Join(' ', path))];
    }

    // Uma falha lista todos os comandos que quebram a regra, não só o primeiro.
    private static void AssertNoProblems(List<string> problems) =>
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

    [Fact]
    public void Arvore_de_comandos_nao_esta_vazia()
    {
        Assert.True(CommandPaths().Length > 10);
    }

    [Fact]
    public void Todo_comando_tem_descricao_em_argumentos_e_opcoes()
    {
        using var harness = new CliHarness();
        var problems = new List<string>();
        foreach (var path in CommandPaths())
        {
            var command = Find(harness, path);
            if (string.IsNullOrWhiteSpace(command.Description))
            {
                problems.Add($"'{path}' sem descrição.");
            }

            problems.AddRange(command.Arguments.Where(a => string.IsNullOrWhiteSpace(a.Description)).Select(a => $"'{path}': argumento <{a.Name}> sem descrição."));
            problems.AddRange(command.Options.Where(o => !o.Hidden && string.IsNullOrWhiteSpace(o.Description)).Select(o => $"'{path}': opção {o.Name} sem descrição."));
        }

        AssertNoProblems(problems);
    }

    [Fact]
    public async Task Ajuda_de_todo_comando_esta_em_portugues()
    {
        var problems = new List<string>();
        foreach (var path in CommandPaths())
        {
            using var harness = new CliHarness();
            var args = path.Length == 0 ? new[] { "--help" } : [.. path.Split(' '), "--help"];

            var result = await harness.RunAsync(args, TestContext.Current.CancellationToken);

            if (result.ExitCode != 0)
            {
                problems.Add($"'{path}': saiu com {result.ExitCode}.");
            }

            foreach (var expected in new[] { "Uso:", "--json", "--yes" }.Where(e => !result.Output.Contains(e, StringComparison.Ordinal)))
            {
                problems.Add($"'{path}': a ajuda não traz '{expected}'.");
            }

            foreach (var marker in EnglishMarkers.Where(m => result.Output.Contains(m, StringComparison.Ordinal)))
            {
                problems.Add($"'{path}': a ajuda traz '{marker}' em inglês.");
            }

            if (EnglishDescription().IsMatch(result.Output))
            {
                problems.Add($"'{path}': a ajuda traz uma descrição que começa com verbo em inglês.");
            }
        }

        AssertNoProblems(problems);
    }

    [Fact]
    public async Task Json_de_todo_comando_devolve_um_envelope_em_uma_linha()
    {
        var problems = new List<string>();
        // `vnf` sem comando mostra a ajuda; --version --json é coberto em RootCommandTests.
        foreach (var path in CommandPaths().Where(p => p.Length > 0))
        {
            // Sem projeto e sem argumentos: cada comando precisa falhar de forma controlada e no formato comum.
            using var harness = new CliHarness();
            var result = await harness.RunAsync([.. path.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--json", "--yes"], TestContext.Current.CancellationToken);

            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (result.Error.Length > 0 || lines.Length != 1)
            {
                problems.Add($"'{path}': esperava uma linha de JSON e nada no erro; veio {lines.Length} linha(s) e erro '{result.Error}'.");
                continue;
            }

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            if (result.ExitCode != root.GetProperty("exitCode").GetInt32())
            {
                problems.Add($"'{path}': exitCode do envelope difere do código de saída {result.ExitCode}.");
            }

            if (root.GetProperty("ok").GetBoolean())
            {
                if (root.TryGetProperty("error", out _))
                {
                    problems.Add($"'{path}': envelope de sucesso traz 'error'.");
                }

                continue;
            }

            var error = root.GetProperty("error");
            if (!ErrorKinds.Contains(error.GetProperty("kind").GetString()))
            {
                problems.Add($"'{path}': kind de erro desconhecido.");
            }

            if (string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()))
            {
                problems.Add($"'{path}': erro sem mensagem.");
            }

            if (lines[0].Contains("   at ", StringComparison.Ordinal))
            {
                problems.Add($"'{path}': o envelope traz stack trace.");
            }
        }

        AssertNoProblems(problems);
    }

    [Fact]
    public async Task Json_de_sucesso_segue_o_formato_comum()
    {
        using var harness = new CliHarness();

        var result = await harness.RunAsync(["config", "ai", "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var line = Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        using var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    private static Command Find(CliHarness harness, string path)
    {
        Command current = harness.Cli.Root;
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
