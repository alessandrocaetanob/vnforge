using System.Text.Json;

namespace VNForge.Cli.Tests;

public sealed class RootCommandTests : IDisposable
{
    private const string JsonOption = "--json";
    private const string Teste = "teste";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Help_esta_em_portugues()
    {
        var result = await _cli.RunAsync(["--help"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Descrição:", result.Output);
        Assert.Contains("Uso:", result.Output);
        Assert.Contains("Opções:", result.Output);
        Assert.Contains("--project <pasta>", result.Output);
        Assert.Contains(JsonOption, result.Output);
        Assert.Contains("--verbose", result.Output);
        Assert.Contains("--quiet", result.Output);
        Assert.Contains("--yes", result.Output);
        Assert.Contains("--no-color", result.Output);
        Assert.Contains("--version", result.Output);
        Assert.Contains("Mostra esta ajuda.", result.Output);
        Assert.DoesNotContain("Description:", result.Output);
        Assert.DoesNotContain("[options]", result.Output);
        Assert.DoesNotContain("Show help", result.Output);
    }

    [Fact]
    public async Task Sem_argumentos_mostra_a_ajuda()
    {
        var result = await _cli.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Uso:", result.Output);
    }

    [Fact]
    public async Task Version_mostra_o_nome_e_a_versao()
    {
        var result = await _cli.RunAsync(["--version"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"vnf {VnfCli.Version}" + Environment.NewLine, result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Fact]
    public async Task Version_com_json_segue_o_formato_comum()
    {
        var result = await _cli.RunAsync(["--version", JsonOption], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var line = Assert.Single(result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("vnf", root.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(VnfCli.Version, root.GetProperty("data").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Opcoes_globais_valem_em_qualquer_posicao_e_chegam_ao_comando()
    {
        Directory.CreateDirectory(Path.Combine(_cli.WorkingDirectory, "meu-projeto"));
        GlobalSettings? seen = null;
        _cli.AddCommand(Teste, parseResult =>
        {
            seen = _cli.Cli.Options.Read(parseResult);
            return 0;
        });

        var result = await _cli.RunAsync(
            ["--project", "meu-projeto", Teste, JsonOption, "--verbose", "--yes"],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new GlobalSettings("meu-projeto", Json: true, Verbose: true, Quiet: false, Yes: true, NoColor: false), seen);
    }

}
