using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    private const string AYbn = "a.ybn";

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // ---- Hash e tamanho ----

    [Theory]
    [InlineData(SyntheticYpfSamples.Script1, 1)]
    [InlineData(SyntheticYpfSamples.Image, -1)]
    public void OpenEntry_TamanhoDescomprimidoDivergenteLanca(string name, int delta)
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var entry = ypf.Entries.Single(e => e.Name == name);
        using var archive = Open(ypf.WithUncompressedSize(name, (uint)(entry.SizeUncompressed + delta)));
        var target = archive.Entries.Single(e => e.Path == name.Replace('\\', '/'));

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(target));

        Assert.Contains(name.Replace('\\', '/'), ex.Message);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(5u)]
    public void OpenEntry_FluxoZlibTruncadoLancaMesmoSemHashConfirmado(uint missing)
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(AYbn, SyntheticYpfSamples.Repetitive, compress: true)
            .Build();
        var entry = ypf.Entries[0];
        using var archive = Open(ypf.WithStoredSize(AYbn, entry.SizeStored - missing));

        Assert.False(archive.Parameters.DataHashVerified);
        Assert.Throws<InvalidDataException>(() => archive.OpenEntry(archive.Entries[0]));
    }

    private static (SyntheticYpf Ypf, SyntheticYpfEntry Entry) SingleCompressed() =>
        SingleCompressed(new SyntheticYpfBuilder(500));

    private static (SyntheticYpf Ypf, SyntheticYpfEntry Entry) SingleCompressed(SyntheticYpfBuilder builder)
    {
        var ypf = builder.AddFile(AYbn, SyntheticYpfSamples.Repetitive, compress: true).Build();
        return (ypf, ypf.Entries[0]);
    }

    [Fact]
    public void OpenEntry_Adler32DoFimErradoComFluxoCompletoLanca()
    {
        var (ypf, entry) = SingleCompressed();
        var bytes = (byte[])ypf.Bytes.Clone();
        bytes[(int)entry.Offset + (int)entry.SizeStored - 1] ^= 0x01;
        using var archive = Open(bytes);

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(archive.Entries[0]));

        Assert.Contains(AYbn, ex.Message);
        Assert.Contains("Adler-32", ex.Message);
    }

    [Fact]
    public void OpenEntry_EntradaAlemDoFimDoArquivoLancaSemAlocar()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var last = ypf.Entries.MaxBy(e => e.Offset + e.SizeStored)!;
        var stream = new MemoryStream(ypf.Bytes);
        using var archive = (YpfArchive)Format.Open(stream);
        stream.SetLength(stream.Length - 1);
        var target = archive.Entries.Single(e => e.Path == last.Name.Replace('\\', '/'));

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(target));

        Assert.Contains("passam do fim do arquivo", ex.Message);
    }

    [Fact]
    public void OpenEntry_TamanhoDescomprimidoImpossivelParaOsBytesComprimidosLanca()
    {
        var (ypf, entry) = SingleCompressed();
        using var archive = Open(ypf.WithUncompressedSize(AYbn, (uint)((long)entry.SizeStored * 1032 + 1)));

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(archive.Entries[0]));

        Assert.Contains("impossível", ex.Message);
    }

    [Fact]
    public void OpenEntry_TamanhoAcimaDe2GiBLancaNotSupported()
    {
        var (ypf, _) = SingleCompressed();
        using var archive = Open(ypf.WithUncompressedSize(AYbn, 0x9000_0000));

        Assert.Throws<NotSupportedException>(() => archive.OpenEntry(archive.Entries[0]));
    }

    [Fact]
    public void OpenEntry_HashDosDadosDivergenteComAlgoritmoConfirmadoLancaEmVezDeDevolverDados()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.WithWrongDataHash(SyntheticYpfSamples.Script1));
        var entry = archive.Entries.Single(e => e.Path == SyntheticYpfSamples.Script1.Replace('\\', '/'));

        Assert.True(archive.Parameters.DataHashVerified);
        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(entry));

        var expected = ypf.Entries.Single(e => e.Name == SyntheticYpfSamples.Script1);
        Assert.Contains("ysbin/yst00001.ybn", ex.Message);
        Assert.Contains($"0x{expected.DataHash:X8}", ex.Message);
        Assert.Contains($"0x{expected.DataHash ^ 1:X8}", ex.Message);
    }
}
