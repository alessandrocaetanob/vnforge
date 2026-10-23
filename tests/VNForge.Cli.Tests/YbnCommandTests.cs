using System.Text.Json;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Cli.Tests;

public sealed class YbnCommandTests : IDisposable
{
    private const string YscYbn = "ysc.ybn";
    private const string YscOption = "--ysc";
    private const string Yst00001Ybn = "yst00001.ybn";
    private const string JsonOption = "--json";
    private const string Arguments = "arguments";
    private const string Value = "value";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string At(params string[] parts) => Path.Combine([_cli.WorkingDirectory, .. parts]);

    private static SyntheticYstb Script()
    {
        var b = new SyntheticYstbBuilder(Yscm);
        b.AddWordBytes([.. GameEncodings.ShiftJis.GetBytes("美咲「やあ"), 0xEF, 0xF0, .. GameEncodings.ShiftJis.GetBytes("またね」"), 0xEF, 0xF2], 1);
        b.AddReturnCode(0, 2);
        b.AddCommand("GOSUB", 3, b.StringExpression("ES.SEL.SET"), b.StringExpression("はい", id: 1), b.StringExpression("いいえ", id: 2));
        b.AddCommand("WAIT", 4, b.ResourceArgument([0x4D, 0x01, 0x00, 0x22], assignOp: 1));
        return b.Build();
    }

    private string WriteScript(string path)
    {
        var full = At(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllBytes(full, Script().Bytes);
        return full;
    }

    private string WriteYsc(string path = YscYbn)
    {
        var full = At(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllBytes(full, Yscm.Bytes);
        return full;
    }

    private static JsonElement Data(RunResult result)
    {
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean(), result.Output);
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task Ybn_lista_o_dump_na_ajuda()
    {
        var result = await _cli.RunAsync(["ybn", "--help"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("dump", result.Output);
    }

    [Fact]
    public async Task Dump_ajuda_cita_a_opcao_ysc()
    {
        var result = await _cli.RunAsync(["ybn", "dump", "--help"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(YscOption, result.Output);
    }

    [Fact]
    public async Task Dump_em_texto_mostra_comandos_argumentos_e_textos_com_marcadores()
    {
        WriteScript(Yst00001Ybn);
        WriteYsc();

        var result = await _cli.RunAsync(["ybn", "dump", Yst00001Ybn, YscOption, YscYbn], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Versão: 500", result.Output);
        Assert.Contains("Chave: 0xD36FAC96", result.Output);
        Assert.Contains("Instruções: 4", result.Output);
        Assert.Contains("#0000  linha 1  WORD", result.Output);
        Assert.Contains("[0] TEXT (texto) = 美咲「やあ\\nまたね」\\p", result.Output);
        Assert.Contains("#0001  linha 2  RETURNCODE", result.Output);
        Assert.Contains("[0] CODE (valor) = tamanho 0, offset 0", result.Output);
        Assert.Contains("[1] PINT1 (string) = はい", result.Output);
        Assert.Contains("[0] TIME (expressão) += 4D010022", result.Output);
    }

    [Fact]
    public async Task Dump_json_traz_o_envelope_com_instrucoes_e_argumentos()
    {
        var script = Script();
        WriteScript(Yst00001Ybn);
        WriteYsc();

        var result = await _cli.RunAsync(["ybn", "dump", Yst00001Ybn, YscOption, YscYbn, JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(At(Yst00001Ybn), data.GetProperty("file").GetString());
        Assert.Equal(At(YscYbn), data.GetProperty("ysc").GetString());
        Assert.Equal(500, data.GetProperty("version").GetInt32());
        Assert.Equal("0xD36FAC96", data.GetProperty("key").GetString());
        Assert.Equal("derived", data.GetProperty("keyOrigin").GetString());
        Assert.Equal(script.Instructions.Count, data.GetProperty("instructionCount").GetInt32());
        Assert.Equal(6, data.GetProperty("argumentCount").GetInt32());

        var instructions = data.GetProperty("instructions");
        Assert.Equal(4, instructions.GetArrayLength());
        var word = instructions[0];
        Assert.Equal("WORD", word.GetProperty("command").GetString());
        Assert.Equal(1, word.GetProperty("line").GetInt32());
        var text = word.GetProperty(Arguments)[0];
        Assert.Equal("text", text.GetProperty("kind").GetString());
        Assert.Equal("TEXT", text.GetProperty("name").GetString());
        Assert.Equal("美咲「やあ\\nまたね」\\p", text.GetProperty(Value).GetString());

        var code = instructions[1].GetProperty(Arguments)[0];
        Assert.Equal(Value, code.GetProperty("kind").GetString());
        Assert.False(code.TryGetProperty(Value, out _));

        Assert.Equal("string", instructions[2].GetProperty(Arguments)[1].GetProperty("kind").GetString());
        var expression = instructions[3].GetProperty(Arguments)[0];
        Assert.Equal("expression", expression.GetProperty("kind").GetString());
        Assert.Equal("4D010022", expression.GetProperty(Value).GetString());
        Assert.Equal("+=", expression.GetProperty("assignOperator").GetString());
    }

    [Fact]
    public async Task Dump_acha_o_ysc_na_pasta_do_script()
    {
        WriteScript(Yst00001Ybn);
        WriteYsc();

        var result = await _cli.RunAsync(["ybn", "dump", Yst00001Ybn, JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(At(YscYbn), Data(result).GetProperty("ysc").GetString());
    }

    [Fact]
    public async Task Dump_acha_o_ysc_na_pasta_acima_do_script()
    {
        WriteScript(Path.Combine("ysbin", Yst00001Ybn));
        WriteYsc();

        var result = await _cli.RunAsync(["ybn", "dump", Path.Combine("ysbin", Yst00001Ybn), JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(At(YscYbn), Data(result).GetProperty("ysc").GetString());
    }

    [Fact]
    public async Task Dump_nao_altera_o_arquivo_de_entrada()
    {
        var script = WriteScript(Yst00001Ybn);
        var ysc = WriteYsc();
        var scriptBefore = await File.ReadAllBytesAsync(script, TestContext.Current.CancellationToken);
        var yscBefore = await File.ReadAllBytesAsync(ysc, TestContext.Current.CancellationToken);

        await _cli.RunAsync(["ybn", "dump", Yst00001Ybn], Token);

        Assert.Equal(scriptBefore, await File.ReadAllBytesAsync(script, TestContext.Current.CancellationToken));
        Assert.Equal(yscBefore, await File.ReadAllBytesAsync(ysc, TestContext.Current.CancellationToken));
        Assert.Equal([YscYbn, Yst00001Ybn], Directory.GetFiles(_cli.WorkingDirectory).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public async Task Dump_sem_ysc_pede_a_opcao_em_portugues()
    {
        WriteScript(Yst00001Ybn);

        var result = await _cli.RunAsync(["ybn", "dump", Yst00001Ybn], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(YscYbn, result.Error);
        Assert.Contains(YscOption, result.Error);
    }

    [Fact]
    public async Task Dump_de_arquivo_inexistente_sai_com_erro()
    {
        WriteYsc();

        var result = await _cli.RunAsync(["ybn", "dump", "nao-existe.ybn"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("O arquivo não existe", result.Error);
    }

    [Fact]
    public async Task Dump_de_ysc_inexistente_sai_com_erro()
    {
        WriteScript(Yst00001Ybn);

        var result = await _cli.RunAsync(["ybn", "dump", Yst00001Ybn, YscOption, "outro.ybn"], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("O arquivo não existe", result.Error);
    }

    [Fact]
    public async Task Dump_de_arquivo_que_nao_e_ystb_sai_com_erro_em_json()
    {
        WriteYsc();
        await File.WriteAllBytesAsync(At("lixo.ybn"), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ybn", "dump", "lixo.ybn", JsonOption], Token);

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("lixo.ybn", document.RootElement.GetProperty("error").GetProperty("message").GetString());
    }
}
