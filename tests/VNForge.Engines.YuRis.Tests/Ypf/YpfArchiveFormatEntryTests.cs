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

    private static void AssertExtractsEverything(SyntheticYpf ypf, YpfArchive archive)
    {
        Assert.Equal(ypf.Entries.Count, archive.Entries.Count);
        for (var i = 0; i < ypf.Entries.Count; i++)
        {
            using var stream = archive.OpenEntry(archive.Entries[i]);

            Assert.Equal(ypf.Entries[i].Content, ReadAll(stream));
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void OpenEntry_ExtraiTodasAsEntradasDeCadaVarianteDoGerador(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SyntheticYpfBuilderTests.SampleBuilder(version, offsetSize, nameHash, dataHash).Build();
        using var archive = Open(ypf.Bytes);

        AssertExtractsEverything(ypf, archive);
    }

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index, false)]
    [InlineData(SyntheticYpfDataOrder.Insertion, false)]
    [InlineData(SyntheticYpfDataOrder.Index, true)]
    [InlineData(SyntheticYpfDataOrder.Insertion, true)]
    public void OpenEntry_ExtraiComOrdemDeDadosEDuplicatasCompartilhadas(SyntheticYpfDataOrder order, bool shared)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300)
            .WithDataOrder(order)
            .WithSharedDuplicateData(shared)
            .Build();
        using var archive = Open(ypf.Bytes);

        AssertExtractsEverything(ypf, archive);
    }

    [Fact]
    public void OpenEntry_ExtraiEntradaComprimidaQueNaoEncolheuQuandoForcada()
    {
        byte[] content = [7, 8, 9];
        var ypf = new SyntheticYpfBuilder(500).AddFile(AYbn, content, forceCompression: true).Build();
        using var archive = Open(ypf.Bytes);

        Assert.True(archive.Entries[0].IsCompressed);
        using var stream = archive.OpenEntry(archive.Entries[0]);
        Assert.Equal(content, ReadAll(stream));
    }

    [Fact]
    public void OpenEntry_DevolveFluxoIndependenteQueSobreviveAoPacote()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var archive = Open(ypf.Bytes);
        var entry = archive.Entries.Single(e => e.Path == SyntheticYpfBuilderTests.Image.Replace('\\', '/'));

        var stream = archive.OpenEntry(entry);
        archive.Dispose();

        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);
        Assert.Equal(SyntheticYpfBuilderTests.Repetitive.Length, stream.Length);
        Assert.Equal(SyntheticYpfBuilderTests.Repetitive, ReadAll(stream));
        stream.Dispose();
    }

    [Fact]
    public void OpenEntry_PodeSerChamadoVariasVezesNaMesmaEntrada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        var entry = archive.Entries[0];

        using var first = archive.OpenEntry(entry);
        using var second = archive.OpenEntry(entry);

        Assert.NotSame(first, second);
        Assert.Equal(ReadAll(first), ReadAll(second));
    }

    [Fact]
    public void OpenEntry_EntradaVaziaDevolveFluxoVazio()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        using var stream = archive.OpenEntry(archive.Entries.Single(e => e.Size == 0));

        Assert.Equal(0, stream.Length);
    }

    // ---- Hash e tamanho ----

    [Fact]
    public void OpenEntry_HashDosDadosDivergenteComAlgoritmoConfirmadoLancaEmVezDeDevolverDados()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.WithWrongDataHash(SyntheticYpfBuilderTests.Script1));
        var entry = archive.Entries.Single(e => e.Path == SyntheticYpfBuilderTests.Script1.Replace('\\', '/'));

        Assert.True(archive.Parameters.DataHashVerified);
        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(entry));

        var expected = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script1);
        Assert.Contains("ysbin/yst00001.ybn", ex.Message);
        Assert.Contains($"0x{expected.DataHash:X8}", ex.Message);
        Assert.Contains($"0x{expected.DataHash ^ 1:X8}", ex.Message);
    }

    [Theory]
    [InlineData(SyntheticYpfBuilderTests.Script1)]
    [InlineData(SyntheticYpfBuilderTests.Image)]
    [InlineData(SyntheticYpfBuilderTests.Sound)]
    public void OpenEntry_DadoCorrompidoLancaEMantemAsOutrasEntradasLegiveis(string name)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(500).Build();
        using var archive = Open(ypf.WithCorruptedData(name));
        var bad = archive.Entries.Single(e => e.Path == name.Replace('\\', '/'));

        Assert.True(archive.Parameters.DataHashVerified);
        Assert.Throws<InvalidDataException>(() => archive.OpenEntry(bad));

        foreach (var (entry, expected) in archive.Entries.Zip(ypf.Entries))
        {
            if (entry != bad && expected.SizeStored > 0)
            {
                using var stream = archive.OpenEntry(entry);
                Assert.Equal(expected.Content, ReadAll(stream));
            }
        }
    }

    [Fact]
    public void OpenEntry_SemAlgoritmoConfirmadoExtraiEExpoeQueOHashNaoFoiVerificado()
    {
        byte[] content = [1, 2, 3, 4];
        var ypf = new SyntheticYpfBuilder(500).AddFile(@"a\um.ybn", content).Build();
        using var archive = Open(ypf.WithWrongDataHash(@"a\um.ybn"));

        Assert.False(archive.Parameters.DataHashVerified);
        using var stream = archive.OpenEntry(archive.Entries[0]);

        Assert.Equal(content, ReadAll(stream));
    }

    [Theory]
    [InlineData(SyntheticYpfBuilderTests.Script1, 1)]
    [InlineData(SyntheticYpfBuilderTests.Script1, -1)]
    [InlineData(SyntheticYpfBuilderTests.Image, 1)]
    [InlineData(SyntheticYpfBuilderTests.Image, -1)]
    public void OpenEntry_TamanhoDescomprimidoDivergenteLanca(string name, int delta)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var entry = ypf.Entries.Single(e => e.Name == name);
        using var archive = Open(ypf.WithUncompressedSize(name, (uint)(entry.SizeUncompressed + delta)));
        var target = archive.Entries.Single(e => e.Path == name.Replace('\\', '/'));

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(target));

        Assert.Contains(name.Replace('\\', '/'), ex.Message);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(5u)]
    public void OpenEntry_FluxoZlibTruncadoLancaMesmoSemHashConfirmado(uint missing)
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(AYbn, SyntheticYpfBuilderTests.Repetitive, compress: true)
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
        var ypf = builder.AddFile(AYbn, SyntheticYpfBuilderTests.Repetitive, compress: true).Build();
        return (ypf, ypf.Entries[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void OpenEntry_BytesDePreenchimentoDepoisDoFluxoZlibSaoTolerados(int padding)
    {
        var (ypf, entry) = SingleCompressed();
        var bytes = ypf.WithStoredSize(AYbn, entry.SizeStored + (uint)padding).Concat(new byte[padding]).ToArray();
        using var archive = Open(bytes);

        Assert.False(archive.Parameters.DataHashVerified);
        using var stream = archive.OpenEntry(archive.Entries[0]);

        Assert.Equal(SyntheticYpfBuilderTests.Repetitive, ReadAll(stream));
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
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
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
    public void OpenEntry_EntradaVaziaMarcadaComoComprimidaLancaPorFaltarOFluxoZlib()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.WithCompressedFlag(SyntheticYpfBuilderTests.Empty, true));
        var entry = archive.Entries.Single(e => e.Size == 0);

        Assert.Throws<InvalidDataException>(() => archive.OpenEntry(entry));
    }

    [Fact]
    public void OpenEntry_EntradaMarcadaComoComprimidaSemSerLancaComOCaminho()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.WithCompressedFlag(SyntheticYpfBuilderTests.Script1, true));
        var entry = archive.Entries.Single(e => e.Path == SyntheticYpfBuilderTests.Script1.Replace('\\', '/'));

        var ex = Assert.Throws<InvalidDataException>(() => archive.OpenEntry(entry));

        Assert.Contains("ysbin/yst00001.ybn", ex.Message);
    }

    [Fact]
    public void OpenEntry_EntradaComprimidaMarcadaComoNaoComprimidaLanca()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.WithCompressedFlag(SyntheticYpfBuilderTests.Image, false));
        var entry = archive.Entries.Single(e => e.Path == SyntheticYpfBuilderTests.Image.Replace('\\', '/'));

        Assert.Throws<InvalidDataException>(() => archive.OpenEntry(entry));
    }
}
