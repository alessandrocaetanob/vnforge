using System.CommandLine;

namespace VNForge.Cli.Tests;

/// <summary>A ajuda da raiz em seções, um exemplo por comando e o nome de valor igual em todas as opções (C4 · #252).</summary>
public sealed class HelpSectionsTests : IDisposable
{
    private static readonly string[] Biblioteca = ["library"];
    private static readonly string[] Pipeline = ["init", "extract", "translate", "review", "check", "build", "inject", "run", "restore", "test", "release"];
    private static readonly string[] Projeto = ["status", "preview", "export", "import", "glossary", "memory", "jmdict", "chars", "bible", "dict", "title", "vndb", "scripts", "lines", "images", "config", "detect", "eval"];
    private static readonly string[] BaixoNivel = ["ypf", "ybn", "patch-exe"];

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<string[]> RootHelpLines()
    {
        var result = await _cli.RunAsync(["--help"], Token);
        Assert.Equal(0, result.ExitCode);
        return result.Output.ReplaceLineEndings("\n").Split('\n');
    }

    private static string[] CommandsUnder(string[] lines, string title)
    {
        var start = Array.IndexOf(lines, title);
        Assert.True(start >= 0, $"Seção '{title}' ausente.");
        return [.. lines.Skip(start + 1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).Select(l => l.Trim().Split(' ')[0])];
    }

    [Fact]
    public async Task Todo_comando_tem_um_exemplo_na_ajuda_que_o_parser_aceita()
    {
        var problems = new List<string>();
        foreach (var path in Leaves())
        {
            var result = await _cli.RunAsync([.. path.Split(' '), "--help"], Token);

            var lines = result.Output.ReplaceLineEndings("\n").Split('\n');
            if (!lines.Contains("Exemplos:"))
            {
                problems.Add($"'{path}': a ajuda não tem a seção 'Exemplos:'.");
                continue;
            }

            var exampleLines = lines.SkipWhile(l => l != "Exemplos:").Skip(1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).Select(l => l.Trim()).ToList();
            if (exampleLines.Count == 0)
            {
                problems.Add($"'{path}': a seção 'Exemplos:' está vazia.");
            }

            foreach (var example in exampleLines)
            {
                if (!example.StartsWith($"vnf {path}", StringComparison.Ordinal))
                {
                    problems.Add($"'{path}': o exemplo '{example}' é de outro comando.");
                    continue;
                }

                var parsed = _cli.Cli.Root.Parse(example["vnf ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (parsed.Errors.Count > 0)
                {
                    problems.Add($"'{example}': {string.Join("; ", parsed.Errors.Select(e => e.Message))}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Todo_exemplo_cadastrado_pertence_a_um_comando_que_existe()
    {
        foreach (var path in CommandExamples.Paths)
        {
            Assert.NotNull(Find(path));
        }
    }

    private IEnumerable<string> Leaves() => Walk(_cli.Cli.Root, []).Where(p => p.Length > 0 && !Subcommands(_cli.Cli.Root, p)).Select(p => string.Join(' ', p));

    private IEnumerable<string> Groups() => Walk(_cli.Cli.Root, []).Where(p => p.Length > 0 && Subcommands(_cli.Cli.Root, p)).Select(p => string.Join(' ', p));

    private static bool Subcommands(RootCommand root, string[] path) => Find(root, string.Join(' ', path)).Subcommands.Count > 0;

    private Command Find(string path) => Find(_cli.Cli.Root, path);

    private static Command Find(RootCommand root, string path)
    {
        Command current = root;
        foreach (var name in path.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Subcommands.Single(c => c.Name == name);
        }

        return current;
    }

    private static IEnumerable<string[]> Walk(Command command, string[] path)
    {
        yield return path;
        foreach (var child in command.Subcommands)
        {
            foreach (var found in Walk(child, [.. path, child.Name]))
            {
                yield return found;
            }
        }
    }
}
