using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli.Tests;

public sealed class PreviewCommandTests : IDisposable
{
    private const string A001000001 = "a001:000001";

    private readonly CliHarness _cli = new();
    private readonly string _projectPath;

    public PreviewCommandTests()
    {
        _projectPath = Path.Combine(_cli.Root, "projeto");
        var project = ProjectFolder.Create(_projectPath, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(_cli.Root, "jogo"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        File.WriteAllText(project.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 2, "charWidth": "uniform" } }""");
        project.SaveText("a001",
        [
            new(A001000001, TextUnitKind.Dialogue, "美咲", "原文", "um dois tres quatro", TranslationStatus.Translated),
            new("a001:000002", TextUnitKind.Dialogue, null, "abcdefghijkl", "abcdefghijkl", TranslationStatus.Translated),
            new("a001:000003", TextUnitKind.Dialogue, null, "原文", "oi", TranslationStatus.Translated),
        ]);
    }

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task Texto_desenha_a_caixa_com_a_regua_do_limite_o_separador_e_os_avisos()
    {
        var result = await _cli.RunAsync(["preview", A001000001, "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain('\u001b', result.Output);
        var lines = Lines(result.Output);
        Assert.Equal("Linha a001:000001", lines[0]);
        Assert.Contains(" 美咲", lines);
        Assert.Contains(lines, l => l.Contains("Caixa 1 · 3 linhas", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" um dois   ┊ ", StringComparison.Ordinal) && l.Contains("7/10 col", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" tres      ┊ ", StringComparison.Ordinal) && l.Contains("4/10 col", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("┄┄┄┄┄┄┄┄┄┄┄┄", StringComparison.Ordinal) && l.Contains("limite de 2 linhas", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" quatro    ┊ ", StringComparison.Ordinal) && l.Contains("acima do limite", StringComparison.Ordinal));
        Assert.Contains("Total: 3 linhas; largura máxima 10 colunas.", lines);
        Assert.Contains(lines, l => l.StartsWith("Aviso: ", StringComparison.Ordinal) && l.Contains("caixa 1 com 3 linhas (máximo 2)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Palavra_mais_larga_que_o_limite_passa_da_regua()
    {
        var result = await _cli.RunAsync(["preview", "a001:000002", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(Lines(result.Output), l => l.Contains(" abcdefghij┊kl ", StringComparison.Ordinal) && l.Contains("12/10 col", StringComparison.Ordinal));
        Assert.Contains("O build não formata esta linha", result.Output);
    }

    [Fact]
    public async Task Terminal_interativo_usa_fundo_escuro_e_coral_no_excesso()
    {
        using var cli = new CliHarness(terminal: CliTerminal.TrueColor(width: 100));

        var result = await cli.RunAsync(["preview", "a001:000002", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("48;2;20;22;28m", result.Output); // fundo da caixa
        Assert.Contains("38;2;241;238;230;48;2;20;22;28m", result.Output); // tinta sobre o fundo
        Assert.Contains("38;2;255;155;133;48;2;20;22;28m", result.Output); // excesso
    }

    [Fact]
    public async Task Linha_sem_falante_nao_desenha_a_placa()
    {
        var result = await _cli.RunAsync(["preview", "a001:000003", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(Lines(result.Output), l => l.StartsWith(' ') && l.Trim() == "美咲");
        Assert.Contains(Lines(result.Output), l => l.Contains(" oi ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Marcadores_nao_desalinham_a_regua_entre_as_linhas()
    {
        var project = ProjectFolder.Open(_projectPath);
        project.SaveText("b001", [new("b001:000001", TextUnitKind.Dialogue, null, "原文", "abc\\cdef\\nxyz", TranslationStatus.Translated)]);

        var result = await _cli.RunAsync(["preview", "b001:000001", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        var rows = Lines(result.Output).Where(l => l.Contains('┊', StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains("abc\\cdef", rows[0], StringComparison.Ordinal);
        Assert.Equal(rows[0].IndexOf('┊', StringComparison.Ordinal), rows[1].IndexOf('┊', StringComparison.Ordinal));
    }

    [Fact]
    public async Task Texto_largo_no_terminal_nao_desalinha_a_borda_do_painel()
    {
        var project = ProjectFolder.Open(_projectPath);
        project.SaveText("c001", [new("c001:000001", TextUnitKind.Dialogue, null, "原文", "美咲\\nab", TranslationStatus.Translated)]);

        var result = await _cli.RunAsync(["preview", "c001:000001", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        var rows = Lines(result.Output).Where(l => l.StartsWith('│')).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains("美咲", rows[0], StringComparison.Ordinal);
        Assert.Equal(TerminalCells(rows[0]), TerminalCells(rows[1]));

        // O fundo escuro de cada linha termina na mesma célula, onde começa o contador.
        var counter = rows.Select(r => TerminalCells(r[..r.IndexOf("2/10 col", StringComparison.Ordinal)])).ToArray();
        Assert.Equal(counter[0], counter[1]);
    }

    // Kanji e kana ocupam duas células no terminal; o resto, uma.
    private static int TerminalCells(string text) => text.Sum(c => c >= '　' ? 2 : 1);

    [Fact]
    public async Task Quebra_desligada_nao_desenha_a_regua()
    {
        File.WriteAllText(ProjectFolder.Open(_projectPath).FormattingPath, """{ "wrap": { "enabled": false } }""");

        var result = await _cli.RunAsync(["preview", A001000001, "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain('┊', result.Output);
        Assert.Contains("(quebra desligada no perfil)", result.Output);
    }

    private static string[] Lines(string output) => [.. output.TrimEnd().ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd())];

    [Fact]
    public async Task Json_traz_texto_caixas_colunas_e_avisos()
    {
        var result = await _cli.RunAsync(["preview", A001000001, "--project", _projectPath, "--json"], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Output);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(A001000001, data.GetProperty("id").GetString());
        Assert.Equal("um dois\\ntres\\nquatro", data.GetProperty("text").GetString());
        Assert.Equal(3, data.GetProperty("lines").GetInt32());
        Assert.Equal(10, data.GetProperty("maxWidth").GetInt32());
        var box = Assert.Single(data.GetProperty("boxes").EnumerateArray());
        Assert.Equal([7, 4, 6], box.GetProperty("columns").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Single(data.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Id_inexistente_sai_com_1_e_mensagem_em_portugues()
    {
        var result = await _cli.RunAsync(["preview", "a001:999999", "--project", _projectPath], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Não existe linha com o id 'a001:999999'", result.Error);
    }
}
