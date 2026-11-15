using System.Text.Json;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

namespace VNForge.Cli.Tests;

[Trait("Risco", "R3")]
public sealed class YpfCommandTests : IDisposable
{
    private const string Unpack = "unpack";
    private const string JsonOption = "--json";
    private const string PortaOgg = "porta.ogg";
    private const string YesOption = "--yes";
    private const string OriginalOption = "--original";
    private const string OutputOption = "--output";
    private const string NovoYpf = "novo.ypf";

    private static readonly byte[] Repetitive = Enumerable.Repeat((byte)'A', 4000).ToArray();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string At(string name) => Path.Combine(_cli.WorkingDirectory, name);

    private static SyntheticYpf Sample() => new SyntheticYpfBuilder(500)
        .AddFile(@"ysbin\yst00001.ybn", [0x10, 0x20, 0x30])
        .AddFile(@"bg\fundo.png", Repetitive, SyntheticYpfFileType.Png, compress: true)
        .AddFile(@"se\porta.ogg", [1, 2, 3, 4, 5], SyntheticYpfFileType.Ogg)
        .Build();

    private string WriteSample(string name = "ysbin.ypf")
    {
        var path = At(name);
        File.WriteAllBytes(path, Sample().Bytes);
        return path;
    }

    private static JsonElement Data(RunResult result)
    {
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean(), result.Output);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<string> ExistingTarget()
    {
        Directory.CreateDirectory(At("out"));
        var old = Path.Combine(At("out"), "antigo.txt");
        await File.WriteAllTextAsync(old, "x", TestContext.Current.CancellationToken);
        return old;
    }

    [Fact]
    public async Task Pack_sem_mudancas_gera_arquivo_identico_ao_original()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);

        var result = await _cli.RunAsync(
            ["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, At(NovoYpf), JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(At(NovoYpf), TestContext.Current.CancellationToken));
        var data = Data(result);
        Assert.Equal(3, data.GetProperty("entryCount").GetInt32());
        Assert.Equal(0, data.GetProperty("replaced").GetArrayLength());
        Assert.Equal(0, data.GetProperty("missing").GetArrayLength());
        Assert.True(data.GetProperty("identical").GetBoolean());
    }

    [Fact]
    public async Task Pack_recusa_gravar_sobre_o_original()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        var before = await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, ypf, YesOption], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("original", result.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Pack_com_arquivo_novo_na_pasta_falha_e_nao_cria_a_saida()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        await File.WriteAllBytesAsync(Path.Combine(At("out"), "extra.bin"), [1], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, At(NovoYpf)], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("extra.bin", result.Error);
        Assert.False(File.Exists(At(NovoYpf)));
        Assert.Empty(Directory.GetFiles(_cli.WorkingDirectory, "*.tmp"));
    }
}
