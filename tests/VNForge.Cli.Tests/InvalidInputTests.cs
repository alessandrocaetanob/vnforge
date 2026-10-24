using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;
using VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

namespace VNForge.Cli.Tests;

/// <summary>YPF e YBN inválidos viram mensagem em português com o arquivo, nunca "Erro inesperado" (C4 · #252).</summary>
public sealed class InvalidInputTests : IDisposable
{
    private const string Caminho = @"C:\jogo\ysbin.ypf";

    private static readonly SyntheticYscm Yscm = new SyntheticYscmBuilder().WithTextCommands().Build();

    private readonly CliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string At(string name) => Path.Combine(_cli.WorkingDirectory, name);

    public static TheoryData<Exception> FormatFailures() => new()
    {
        new EndOfStreamException("Unable to read beyond the end of the stream."),
        new InvalidDataException("Found invalid data while decoding."),
        new NotSupportedException("Specified method is not supported."),
        new FormatException("Input string was not in a correct format."),
        new OverflowException("Arithmetic operation resulted in an overflow."),
    };

    [Theory]
    [MemberData(nameof(FormatFailures))]
    public void Falha_de_formato_vira_arquivo_invalido_em_portugues_com_o_caminho(Exception failure)
    {
        var thrown = Assert.Throws<InvalidFileException>(() => InputFileGuard.Read<int>(Caminho, () => throw failure));

        Assert.Contains(Caminho, thrown.Message);
        Assert.Contains("é inválido", thrown.Message);
        Assert.DoesNotContain(failure.Message, thrown.Message);
        Assert.Same(failure, thrown.InnerException);
    }

    [Fact]
    public void Falha_de_leitura_do_disco_vira_nao_foi_possivel_ler_com_o_caminho()
    {
        var thrown = Assert.Throws<VNForgeException>(() => InputFileGuard.Read<int>(Caminho, () => throw new IOException("disk")));

        Assert.Equal($"Não foi possível ler \"{Caminho}\": disk", thrown.Message);
    }

    [Fact]
    public void Erro_esperado_passa_sem_mudar_mesmo_sem_citar_o_caminho()
    {
        var original = new VNForgeException("Falha qualquer.");

        var thrown = Assert.Throws<VNForgeException>(() => InputFileGuard.Read<int>(Caminho, () => throw original));

        Assert.Same(original, thrown);
    }

    [Fact]
    public void Erro_esperado_que_ja_cita_o_caminho_passa_sem_mudar()
    {
        var original = new InvalidFileException(Caminho, "truncado");

        var thrown = Assert.Throws<InvalidFileException>(() => InputFileGuard.Read<int>(Caminho, () => throw original));

        Assert.Same(original, thrown);
    }

    [Fact]
    public void Bug_de_verdade_nao_e_mascarado()
    {
        Assert.Throws<InvalidOperationException>(() => InputFileGuard.Read<int>(Caminho, () => throw new InvalidOperationException("bug")));
    }

    [Fact]
    public async Task Ypf_truncado_cita_o_arquivo_em_portugues()
    {
        var bytes = new SyntheticYpfBuilder(500).AddFile(@"ysbin\yst00001.ybn", [1, 2, 3]).Build().Bytes;
        await File.WriteAllBytesAsync(At("cortado.ypf"), bytes[..(bytes.Length / 2)], Token);

        var result = await _cli.RunAsync(["ypf", "info", At("cortado.ypf")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Erro: ", result.Error, StringComparison.Ordinal);
        Assert.Contains(At("cortado.ypf"), result.Error);
        Assert.DoesNotContain("Erro inesperado", result.Error);
    }

    [Fact]
    public async Task Unpack_de_ypf_com_entrada_corrompida_cita_o_ypf_e_a_entrada()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile(@"bg\fundo.png", Enumerable.Repeat((byte)'A', 4000).ToArray(), SyntheticYpfFileType.Png, compress: true).Build();
        var bytes = ypf.Bytes;
        bytes[(int)ypf.Entries[0].Offset + 4] ^= 0xFF;
        await File.WriteAllBytesAsync(At("ruim.ypf"), bytes, Token);

        var result = await _cli.RunAsync(["ypf", "unpack", At("ruim.ypf"), At("saida")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("fundo.png", result.Error);
        Assert.Contains(At("ruim.ypf"), result.Error);
        Assert.DoesNotContain("Erro inesperado", result.Error);
    }

    [Fact]
    public async Task Ybn_que_nao_e_ystb_cita_o_arquivo()
    {
        await File.WriteAllBytesAsync(At("ysc.ybn"), Yscm.Bytes, Token);
        await File.WriteAllBytesAsync(At("yst00001.ybn"), new byte[64], Token);

        var result = await _cli.RunAsync(["ybn", "dump", At("yst00001.ybn")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(At("yst00001.ybn"), result.Error);
        Assert.Contains("inválido", result.Error);
    }

    [Fact]
    public async Task Ysc_truncado_cita_o_ysc_e_nao_o_ingles_do_stream()
    {
        var script = new SyntheticYstbBuilder(Yscm).AddReturnCode(0, 2).Build().Bytes;
        await File.WriteAllBytesAsync(At("yst00001.ybn"), script, Token);
        await File.WriteAllBytesAsync(At("ysc.ybn"), Yscm.Bytes[..10], Token);

        var result = await _cli.RunAsync(["ybn", "dump", At("yst00001.ybn")], Token);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(At("ysc.ybn"), result.Error);
        Assert.DoesNotContain("Unable to read", result.Error);
        Assert.DoesNotContain("Erro inesperado", result.Error);
    }
}
