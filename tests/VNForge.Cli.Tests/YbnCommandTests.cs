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
