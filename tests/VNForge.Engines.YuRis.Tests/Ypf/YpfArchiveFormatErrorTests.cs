using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    // ---- Vetores de ouro ----

    // Os três vetores do SyntheticYpfSamples foram montados fora do gerador: uma entrada "bg\scene_0001.png", conteúdo "ABC".
    private readonly record struct GoldenVector(
        uint Version,
        uint HeaderSize,
        bool IncludesHeader,
        YpfHashAlgorithm NameHash,
        YpfHashAlgorithm DataHash,
        YpfOffsetSize OffsetSize,
        YpfSwapTable SwapTable,
        byte NameXor,
        uint ExpectedNameHash,
        uint ExpectedDataHash,
        ulong ExpectedOffset);

    private static void AssertGolden(string hex, GoldenVector golden)
    {
        using var archive = Open(Convert.FromHexString(hex));

        var entry = Assert.Single(archive.Entries);
        Assert.Equal("bg/scene_0001.png", entry.Path);
        Assert.Equal(3, entry.Size);
        Assert.False(entry.IsCompressed);
        Assert.Equal((byte)2, entry.FileType);
        Assert.Equal(3u, entry.SizeStored);
        Assert.Equal(golden.ExpectedOffset, entry.Offset);
        Assert.Equal(golden.ExpectedNameHash, entry.NameHash);
        Assert.Equal(golden.ExpectedDataHash, entry.DataHash);
        Assert.Equal(new YpfHeader(golden.Version, 1, golden.HeaderSize, new byte[YpfHeader.PaddingSize]), archive.Header);
        Assert.Equal(golden.IncludesHeader, archive.Parameters.HeaderSizeIncludesHeader);
        Assert.Equal(golden.NameHash, archive.Parameters.NameHash);
        Assert.Equal(golden.DataHash, archive.Parameters.DataHash);
        Assert.Equal(golden.OffsetSize, archive.Parameters.OffsetSize);
        Assert.Equal(golden.NameXor, archive.Parameters.NameXor);
        Assert.Equal(golden.SwapTable, entry.SwapTable);
    }

    [Fact]
    public void Open_le_os_vetores_de_ouro_montados_a_mao_em_v200_v300_e_v500()
    {
        AssertGolden(
            SyntheticYpfSamples.GoldenV200,
            new GoldenVector(
                200, 0x28, false,
                YpfHashAlgorithm.Adler32, YpfHashAlgorithm.Adler32, YpfOffsetSize.Bits32, YpfSwapTable.Standard,
                0xFF, 0x36A705C7, 0x018D00C7, 0x48));
        AssertGolden(
            SyntheticYpfSamples.GoldenV300,
            new GoldenVector(
                300, 0x48, true,
                YpfHashAlgorithm.Crc32, YpfHashAlgorithm.Adler32, YpfOffsetSize.Bits32, YpfSwapTable.Standard,
                0xFF, 0xE582ABD0, 0x018D00C7, 0x48));
        AssertGolden(
            SyntheticYpfSamples.GoldenV500,
            new GoldenVector(
                500, 0x4C, true,
                YpfHashAlgorithm.MurmurHash2, YpfHashAlgorithm.MurmurHash2, YpfOffsetSize.Bits64, YpfSwapTable.V500,
                0xC9, 0xE099F84B, 0xFD17A8FF, 0x4C));
    }

    // ---- Erros ----

    private static void AssertInvalid(byte[] bytes, params string[] expectedFragments)
    {
        var ex = Assert.Throws<InvalidFileException>(() => Open(bytes));
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, ex.Message);
        }
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(199u)]
    [InlineData(600u)]
    [InlineData(9999u)]
    public void Open_VersaoDesconhecidaComIndiceIlegivelLancaUnknownVersionException(uint version)
    {
        var ypf = SyntheticYpfSamples.DefaultSample(version).Build();

        var ex = Assert.Throws<UnknownVersionException>(() => Open(ypf.WithWrongNameHash(ypf.Entries[2].Name)));

        Assert.StartsWith(version.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Version);
        Assert.Contains("Versão desconhecida", ex.Message);
        Assert.Contains("fora das versões conhecidas", ex.Message);
        Assert.Contains("nenhuma combinação", ex.Message);
        Assert.IsType<InvalidFileException>(ex.InnerException);
    }

    // ---- CanOpen e ciclo de vida ----

    [Fact]
    public void Open_ErroNaoFechaOFluxoDeQuemChamou()
    {
        using var stream = new MemoryStream(new byte[64]);

        Assert.Throws<InvalidFileException>(() => Format.Open(stream));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Dispose_FechaOFluxoDeOrigem()
    {
        var stream = new MemoryStream(SyntheticYpfSamples.DefaultSample(300).Build().Bytes);
        var archive = Format.Open(stream);

        archive.Dispose();
#pragma warning disable S3966 // o teste verifica justamente que um segundo Dispose é inofensivo
        archive.Dispose();
#pragma warning restore S3966

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void OpenEntry_RejeitaEntradaDeOutroPacoteENula()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var other = Open(ypf.Bytes);

        Assert.Throws<ArgumentException>(() => archive.OpenEntry(other.Entries[0]));
        Assert.Throws<ArgumentException>(() => archive.OpenEntry(new ArchiveEntry("x", 1, false)));
        Assert.Throws<ArgumentNullException>(() => archive.OpenEntry(null!));
    }

    // ---- Tabelas de troca e hash ----
}
