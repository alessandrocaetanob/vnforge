using System.Text.Json;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

namespace VNForge.Cli.Tests;

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

    [Fact]
    public async Task Ypf_lista_os_tres_subcomandos_na_ajuda()
    {
        var result = await _cli.RunAsync(["ypf", "--help"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("info", result.Output);
        Assert.Contains(Unpack, result.Output);
        Assert.Contains("pack", result.Output);
    }

    [Fact]
    public async Task Info_mostra_versao_variante_e_arquivos()
    {
        var ypf = WriteSample();

        var result = await _cli.RunAsync(["ypf", "info", ypf], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Versão: 500", result.Output);
        Assert.Contains("Arquivos: 3", result.Output);
        Assert.Contains("ysbin/yst00001.ybn", result.Output);
        Assert.Contains("bg/fundo.png", result.Output);
        Assert.Contains("se/porta.ogg", result.Output);
    }

    [Fact]
    public async Task Info_json_traz_variante_hashes_e_lista_de_arquivos()
    {
        var sample = Sample();
        var ypf = WriteSample();

        var result = await _cli.RunAsync(["ypf", "info", ypf, JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(500, data.GetProperty("version").GetInt32());
        Assert.Equal(3, data.GetProperty("fileCount").GetInt32());
        Assert.Equal(sample.Bytes.Length, data.GetProperty("fileLength").GetInt64());
        Assert.Equal(sample.NameHashAlgorithm.ToString(), data.GetProperty("nameHash").GetString());
        Assert.Equal(sample.DataHashAlgorithm.ToString(), data.GetProperty("dataHash").GetString());
        var files = data.GetProperty("files");
        Assert.Equal(3, files.GetArrayLength());
        var image = files.EnumerateArray().Single(f => f.GetProperty("path").GetString() == "bg/fundo.png");
        Assert.Equal(Repetitive.Length, image.GetProperty("size").GetInt64());
        Assert.True(image.GetProperty("compressed").GetBoolean());
        var expected = sample.Entries.Single(e => e.Name == @"bg\fundo.png");
        Assert.Equal($"0x{expected.NameHash:X8}", image.GetProperty("nameHash").GetString());
        Assert.Equal($"0x{expected.DataHash:X8}", image.GetProperty("dataHash").GetString());
    }

    [Fact]
    public async Task Info_de_arquivo_inexistente_falha_em_portugues()
    {
        var result = await _cli.RunAsync(["ypf", "info", At("nada.ypf"), JsonOption], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("O arquivo não existe", result.Output);
        Assert.Contains("\"ok\":false", result.Output);
    }

    [Fact]
    public async Task Info_de_arquivo_que_nao_e_ypf_falha()
    {
        await File.WriteAllBytesAsync(At("lixo.ypf"), new byte[64], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ypf", "info", At("lixo.ypf")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("inválido", result.Error);
    }

    [Fact]
    public async Task Unpack_json_extrai_para_a_pasta()
    {
        var ypf = WriteSample();

        var result = await _cli.RunAsync(["ypf", Unpack, ypf, At("out"), JsonOption], Token);

        Assert.Equal(0, result.ExitCode);
        var data = Data(result);
        Assert.Equal(3, data.GetProperty("fileCount").GetInt32());
        Assert.Equal(3 + 4000 + 5, data.GetProperty("byteCount").GetInt64());
        Assert.Equal(Repetitive, await File.ReadAllBytesAsync(Path.Combine(At("out"), "bg", "fundo.png"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unpack_resolve_caminho_relativo_pela_pasta_atual()
    {
        WriteSample();

        var result = await _cli.RunAsync(["ypf", Unpack, "ysbin.ypf", "saida"], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Extraídos 3 arquivos", result.Output);
        Assert.True(File.Exists(Path.Combine(At("saida"), "se", PortaOgg)));
    }

    [Fact]
    public async Task Unpack_em_pasta_com_arquivos_pede_yes()
    {
        var ypf = WriteSample();
        Directory.CreateDirectory(At("out"));
        await File.WriteAllTextAsync(Path.Combine(At("out"), "antigo.txt"), "x", TestContext.Current.CancellationToken);

        var refused = await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains(YesOption, refused.Error);
        Assert.False(File.Exists(Path.Combine(At("out"), "se", PortaOgg)));

        var accepted = await _cli.RunAsync(["ypf", Unpack, ypf, At("out"), YesOption], Token);
        Assert.Equal(0, accepted.ExitCode);
        Assert.True(File.Exists(Path.Combine(At("out"), "se", PortaOgg)));
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
    public async Task Pack_com_arquivo_alterado_lista_a_troca_e_nao_mexe_no_original()
    {
        var ypf = WriteSample();
        var before = await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken);
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        await File.WriteAllBytesAsync(Path.Combine(At("out"), "se", PortaOgg), [9, 9], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(
            ["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, At(NovoYpf)], Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Trocados 1", result.Output);
        Assert.Contains("se/porta.ogg", result.Output);
        Assert.Equal(before, await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken));
        Assert.NotEqual(before, await File.ReadAllBytesAsync(At(NovoYpf), TestContext.Current.CancellationToken));
        var reunpack = await _cli.RunAsync(["ypf", Unpack, At(NovoYpf), At("out2")], Token);
        Assert.Equal(0, reunpack.ExitCode);
        Assert.Equal(new byte[] { 9, 9 }, await File.ReadAllBytesAsync(Path.Combine(At("out2"), "se", PortaOgg), TestContext.Current.CancellationToken));
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
    public async Task Pack_pede_yes_para_sobrescrever_a_saida_e_nao_deixa_temporario()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        await File.WriteAllTextAsync(At(NovoYpf), "antigo", TestContext.Current.CancellationToken);

        var refused = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, At(NovoYpf)], Token);
        Assert.Equal(1, refused.ExitCode);
        Assert.Equal("antigo", await File.ReadAllTextAsync(At(NovoYpf), TestContext.Current.CancellationToken));

        var accepted = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, At(NovoYpf), YesOption], Token);
        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(ypf, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(At(NovoYpf), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_cli.WorkingDirectory, "*.tmp"));
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

    [Fact]
    public async Task Pack_recusa_saida_dentro_da_pasta_empacotada()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);

        var result = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, Path.Combine(At("out"), NovoYpf)], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("dentro da pasta", result.Error);
    }

    [Fact]
    public async Task Unpack_de_hash_adulterado_sai_como_erro_esperado()
    {
        File.WriteAllBytes(At("ruim.ypf"), Sample().WithWrongDataHash(@"se\porta.ogg"));

        var result = await _cli.RunAsync(["ypf", Unpack, At("ruim.ypf"), At("out")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("se/porta.ogg", result.Error);
        Assert.DoesNotContain("inesperado", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unpack_com_destino_que_e_arquivo_sai_como_erro_esperado()
    {
        var ypf = WriteSample();
        await File.WriteAllTextAsync(At("destino"), "x", TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ypf", Unpack, ypf, At("destino")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Não foi possível gravar", result.Error);
    }

    [Fact]
    public async Task Pack_com_pasta_invalida_nao_cria_a_pasta_de_saida()
    {
        var ypf = WriteSample();
        await _cli.RunAsync(["ypf", Unpack, ypf, At("out")], Token);
        await File.WriteAllBytesAsync(Path.Combine(At("out"), "Thumbs.db"), [1], TestContext.Current.CancellationToken);

        var result = await _cli.RunAsync(["ypf", "pack", At("out"), OriginalOption, ypf, OutputOption, Path.Combine(At("novo"), "x.ypf")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Thumbs.db", result.Error);
        Assert.False(Directory.Exists(At("novo")));
    }

    [Fact]
    public async Task Pack_exige_original_e_output()
    {
        var result = await _cli.RunAsync(["ypf", "pack", At("out")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(OriginalOption, result.Error);
    }
}
