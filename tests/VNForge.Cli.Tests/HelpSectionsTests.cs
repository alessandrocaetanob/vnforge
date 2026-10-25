using System.CommandLine;

namespace VNForge.Cli.Tests;

/// <summary>A ajuda da raiz em seções, um exemplo por comando e o nome de valor igual em todas as opções (C4 · #252).</summary>
public sealed class HelpSectionsTests : IDisposable
{
    private static readonly string[] Pipeline = ["init", "extract", "translate", "review", "check", "build", "inject", "run", "restore"];
    private static readonly string[] Projeto = ["status", "preview", "export", "import", "glossary", "chars", "config", "detect"];
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
    public async Task Raiz_agrupa_os_comandos_em_secoes_na_ordem_do_pipeline()
    {
        var lines = await RootHelpLines();

        Assert.Equal(Pipeline, CommandsUnder(lines, "Pipeline:"));
        Assert.Equal(Projeto, CommandsUnder(lines, "Projeto:"));
        Assert.Equal(BaixoNivel, CommandsUnder(lines, "Baixo nível:"));
        var order = new[] { "Pipeline:", "Projeto:", "Baixo nível:", "Opções:" }.Select(t => Array.IndexOf(lines, t)).ToArray();
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.All(order, i => Assert.True(i >= 0));
        Assert.DoesNotContain("Comandos:", lines);
    }

    [Fact]
    public async Task Comando_fora_das_secoes_cai_em_outros_comandos()
    {
        _cli.AddCommand("teste", _ => 0);

        var lines = await RootHelpLines();

        Assert.Equal(["teste"], CommandsUnder(lines, "Outros comandos:"));
    }

    [Fact]
    public void Toda_secao_aponta_para_comandos_que_existem_e_nenhum_fica_de_fora()
    {
        var registered = _cli.Cli.Root.Subcommands.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var sectioned = VnfCommands.Sections.SelectMany(s => s.Commands).ToList();

        Assert.Equal(sectioned.Count, sectioned.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sectioned, name => Assert.Contains(name, registered));
        Assert.Empty(registered.Except(sectioned, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Patch_exe_e_um_comando_de_baixo_nivel_marcado_como_experimental()
    {
        Assert.Contains(_cli.Cli.Root.Subcommands, c => c.Name == "patch-exe");

        var lines = await RootHelpLines();

        var line = lines.Single(l => l.TrimStart().StartsWith("patch-exe", StringComparison.Ordinal));
        Assert.Contains("Experimental", line);
        Assert.Contains("patch-exe", CommandsUnder(lines, "Baixo nível:"));
    }

    [Fact]
    public void Descricao_e_de_uma_linha()
    {
        foreach (var path in Leaves().Concat(Groups()))
        {
            var description = Find(path).Description;
            Assert.False(string.IsNullOrWhiteSpace(description), path);
            Assert.DoesNotContain('\n', description);
            Assert.True(description.Length <= 100, $"'{path}' tem {description.Length} caracteres; a descrição cabe em uma linha de até 100.");
        }
    }

    public static TheoryData<string> LeafPaths()
    {
        using var harness = new CliHarness();
        var data = new TheoryData<string>();
        foreach (var path in Walk(harness.Cli.Root, []).Where(p => p.Length > 0 && !Subcommands(harness.Cli.Root, p)))
        {
            data.Add(string.Join(' ', path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LeafPaths))]
    public async Task Todo_comando_tem_um_exemplo_na_ajuda_que_o_parser_aceita(string path)
    {
        var result = await _cli.RunAsync([.. path.Split(' '), "--help"], Token);

        var lines = result.Output.ReplaceLineEndings("\n").Split('\n');
        var examples = CommandsUnder(lines, "Exemplos:");
        Assert.NotEmpty(examples);
        var exampleLines = lines.SkipWhile(l => l != "Exemplos:").Skip(1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).Select(l => l.Trim());
        foreach (var example in exampleLines)
        {
            Assert.StartsWith($"vnf {path}", example, StringComparison.Ordinal);
            var parsed = _cli.Cli.Root.Parse(example["vnf ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            Assert.True(parsed.Errors.Count == 0, $"'{example}': {string.Join("; ", parsed.Errors.Select(e => e.Message))}");
        }
    }

    [Fact]
    public void Todo_exemplo_cadastrado_pertence_a_um_comando_que_existe()
    {
        foreach (var path in CommandExamples.Paths)
        {
            Assert.NotNull(Find(path));
        }
    }

    [Fact]
    public void Opcoes_com_o_mesmo_nome_usam_o_mesmo_nome_de_valor()
    {
        var byName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var path in Walk(_cli.Cli.Root, []))
        {
            var command = path.Length == 0 ? _cli.Cli.Root : Find(string.Join(' ', path));
            foreach (var option in command.Options.Where(o => o.ValueType != typeof(bool) && o is not System.CommandLine.Help.HelpOption))
            {
                if (!byName.TryGetValue(option.Name, out var names))
                {
                    byName[option.Name] = names = [];
                }

                names.Add(option.HelpName ?? "valor");
            }
        }

        Assert.All(byName, pair => Assert.True(pair.Value.Count == 1, $"{pair.Key}: {string.Join(", ", pair.Value)}"));
        Assert.Equal("modelo", Assert.Single(byName["--model"]));
        Assert.Equal("pasta", Assert.Single(byName["--game"]));
        Assert.Equal("pasta", Assert.Single(byName["--project"]));
        Assert.DoesNotContain(byName.Values.SelectMany(v => v), name => name is "dir" or "id");
    }

    [Theory]
    [InlineData("init", "--game <pasta>")]
    [InlineData("init", "--model <modelo>")]
    [InlineData("detect", "--game <pasta>")]
    [InlineData("translate", "--model <modelo>")]
    [InlineData("review", "--model <modelo>")]
    [InlineData("config ai", "--model <modelo>")]
    public async Task Ajuda_mostra_pasta_e_modelo_como_nome_do_valor(string path, string expected)
    {
        var result = await _cli.RunAsync([.. path.Split(' '), "--help"], Token);

        Assert.Contains(expected, result.Output);
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
