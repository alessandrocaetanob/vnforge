using System.CommandLine;
using System.Text.Json;
using VNForge.Core;
using VNForge.Core.Projects;

namespace VNForge.Cli.Tests;

/// <summary>Erro com uma segunda linha de dica ("→ ..."), só no texto; o <c>--json</c> segue igual (C4 · #252).</summary>
public sealed class ErrorHintTests : IDisposable
{
    private const string Seta = "→ ";

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    [Fact]
    public async Task Pasta_do_projeto_inexistente_diz_como_resolver()
    {
        var result = await _cli.RunAsync(["--project", "nada", "status"], Token);

        Assert.Equal(1, result.ExitCode);
        var lines = Lines(result.Error);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Erro: A pasta do projeto não existe: ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith(Seta, lines[1], StringComparison.Ordinal);
        Assert.Contains("vnf init", lines[1]);
        Assert.Contains("--project", lines[1]);
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_diz_para_criar_ou_apontar_o_projeto()
    {
        var result = await _cli.RunAsync(["status"], Token);

        Assert.Equal(1, result.ExitCode);
        var lines = Lines(result.Error);
        Assert.Equal(2, lines.Length);
        Assert.Contains(ProjectFolder.ConfigFileName, lines[0]);
        Assert.StartsWith(Seta, lines[1], StringComparison.Ordinal);
        Assert.Contains("--project <pasta>", lines[1]);
        Assert.Contains("vnf init", lines[1]);
    }

    [Fact]
    public async Task Erro_de_uso_do_parser_aponta_a_ajuda_do_comando_na_segunda_linha()
    {
        var result = await _cli.RunAsync(["init"], Token);

        Assert.Equal(1, result.ExitCode);
        var lines = Lines(result.Error);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Erro: ", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("--help", lines[0]);
        Assert.Equal($"{Seta}Rode 'vnf init --help' para ver as opções.", lines[1]);
    }

    [Fact]
    public async Task Erro_de_uso_na_raiz_aponta_a_ajuda_da_raiz()
    {
        var result = await _cli.RunAsync(["--bogus"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"{Seta}Rode 'vnf --help' para ver as opções.", Lines(result.Error)[^1]);
    }

    [Fact]
    public async Task Arquivo_invalido_do_jogo_ganha_dica_e_o_json_nao()
    {
        _cli.AddCommand("falha", _ => throw new InvalidFileException("a.ypf", "truncado"));

        var text = await _cli.RunAsync(["falha"], Token);
        var json = await _cli.RunAsync(["falha", "--json"], Token);

        Assert.Equal(1, text.ExitCode);
        Assert.Equal("Erro: O arquivo 'a.ypf' é inválido: truncado", Lines(text.Error)[0]);
        Assert.StartsWith(Seta, Lines(text.Error)[1], StringComparison.Ordinal);
        Assert.Equal(string.Empty, json.Error);
        Assert.DoesNotContain('→', json.Output);
    }

    [Fact]
    public async Task Pasta_do_jogo_inexistente_no_detect_diz_qual_opcao_usar()
    {
        var result = await _cli.RunAsync(["detect", "--game", "nada"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A pasta do jogo não existe", Lines(result.Error)[0]);
        Assert.Equal($"{Seta}Informe a pasta do jogo com --game <pasta>.", Lines(result.Error)[1]);
    }

    [Fact]
    public async Task Json_nao_ganha_a_linha_de_dica()
    {
        var result = await _cli.RunAsync(["--project", "nada", "status", "--json"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        var line = Assert.Single(Lines(result.Output));
        using var document = JsonDocument.Parse(line);
        var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.StartsWith("A pasta do projeto não existe: ", message, StringComparison.Ordinal);
        Assert.DoesNotContain('→', line);
    }

    [Fact]
    public async Task Erro_sem_dica_cadastrada_continua_com_uma_linha()
    {
        _cli.AddCommand("falha", _ => throw new UsageException("Algo que ninguém previu."));

        var result = await _cli.RunAsync(["falha"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Erro: Algo que ninguém previu.", Assert.Single(Lines(result.Error)));
    }

    [Fact]
    public async Task Erro_inesperado_nao_ganha_dica()
    {
        _cli.AddCommand("quebra", _ => throw new InvalidOperationException("estado quebrado"));

        var result = await _cli.RunAsync(["quebra"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain(Seta, result.Error);
    }

    [Fact]
    public async Task Dica_de_uma_UsageException_vem_depois_da_mensagem()
    {
        _cli.AddCommand("falha", _ => throw new UsageException("Valor ruim.", "Troque o valor."));

        var result = await _cli.RunAsync(["falha"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["Erro: Valor ruim.", $"{Seta}Troque o valor."], Lines(result.Error));
    }

    [Fact]
    public async Task Cancelamento_nao_ganha_dica()
    {
        _cli.AddCommand("cancela", (_, _) => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await _cli.RunAsync(["cancela"], cts.Token);

        Assert.Equal(3, result.ExitCode);
        Assert.DoesNotContain(Seta, result.Error);
    }

    [Fact]
    public async Task Mensagem_de_parse_sem_traducao_sai_sem_ingles_e_o_original_vai_para_o_log()
    {
        var command = new Command("valida", "Comando de teste.");
        command.Validators.Add(result => result.AddError("File does not exist: 'x'."));
        command.SetAction(_ => 0);
        _cli.Cli.Root.Subcommands.Add(command);

        var result = await _cli.RunAsync(["valida"], Token);

        Assert.Equal(1, result.ExitCode);
        var lines = Lines(result.Error);
        Assert.Equal("Erro: Uso inválido.", lines[0]);
        Assert.Equal($"{Seta}Rode 'vnf valida --help' para ver as opções.", lines[1]);
        Assert.DoesNotContain("File does not exist", result.Error);
        var log = Assert.Single(Directory.GetFiles(_cli.UserLogDirectory, "*.log"));
        Assert.Contains("File does not exist: 'x'.", File.ReadAllText(log));
    }

    [Fact]
    public async Task Verbose_com_quiet_continua_com_a_mensagem_propria_em_portugues()
    {
        var result = await _cli.RunAsync(["--verbose", "--quiet"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("Erro: As opções --verbose e --quiet não podem ser usadas juntas.", Lines(result.Error)[0]);
    }
}
