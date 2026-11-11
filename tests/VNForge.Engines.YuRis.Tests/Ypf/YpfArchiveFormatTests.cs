using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    private static readonly YpfArchiveFormat Format = new();

    private static YpfArchive Open(byte[] bytes) => (YpfArchive)Format.Open(new MemoryStream(bytes));

    private static YpfHashAlgorithm Map(SyntheticYpfHashAlgorithm a) => a switch
    {
        SyntheticYpfHashAlgorithm.Adler32 => YpfHashAlgorithm.Adler32,
        SyntheticYpfHashAlgorithm.Crc32 => YpfHashAlgorithm.Crc32,
        SyntheticYpfHashAlgorithm.MurmurHash2 => YpfHashAlgorithm.MurmurHash2,
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    private static YpfOffsetSize Map(SyntheticYpfOffsetSize s) =>
        s == SyntheticYpfOffsetSize.Bits64 ? YpfOffsetSize.Bits64 : YpfOffsetSize.Bits32;

    private static YpfSwapTable Map(SyntheticYpfSwapTable t) => t switch
    {
        SyntheticYpfSwapTable.Standard => YpfSwapTable.Standard,
        SyntheticYpfSwapTable.V500 => YpfSwapTable.V500,
        SyntheticYpfSwapTable.Garbro04 => YpfSwapTable.Garbro04,
        SyntheticYpfSwapTable.Garbro10 => YpfSwapTable.Garbro10,
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    /// <summary>Confere o cabeçalho, os parâmetros detectados e cada campo de cada entrada contra o descritor do gerador.</summary>
    private static void AssertMatches(SyntheticYpf ypf, YpfArchive archive)
    {
        Assert.Equal(new YpfHeader(ypf.Version, ypf.FileCount, ypf.HeaderSize, new byte[YpfHeader.PaddingSize]), archive.Header);
        Assert.True(archive.Header.HasZeroPadding);

        var p = archive.Parameters;
        Assert.Equal(ypf.Version, p.Version);
        Assert.Equal(Map(ypf.NameHashAlgorithm), p.NameHash);
        Assert.Equal(Map(ypf.DataHashAlgorithm), p.DataHash);
        Assert.Equal(Map(ypf.OffsetSize), p.OffsetSize);
        Assert.Equal(ypf.HeaderSizeIncludesHeader, p.HeaderSizeIncludesHeader);
        Assert.Equal(ypf.EffectiveNameXor, p.NameXor);
        Assert.Equal(ypf.NameKey, p.NameKey);
        Assert.Equal((int)ypf.DataStart, p.DataStart);
        Assert.Equal(ypf.HeaderSize, p.ExpectedHeaderSize);
        Assert.True(p.DataHashVerified);
        Assert.Equal(ypf.Bytes.Length, p.FileLength);
        Assert.Empty(p.Gaps);
        Assert.Equal(new YpfByteRange(ypf.Bytes.Length, 0), p.Trailer);

        Assert.Equal(ypf.Entries.Count, archive.Entries.Count);
        for (var i = 0; i < ypf.Entries.Count; i++)
        {
            var expected = ypf.Entries[i];
            var actual = archive.Entries[i];

            Assert.Equal(expected.Name.Replace('\\', '/'), actual.Path);
            Assert.Equal((long)expected.SizeUncompressed, actual.Size);
            Assert.Equal(expected.IsCompressed, actual.IsCompressed);
            Assert.Equal(expected.NameHash, actual.NameHash);
            Assert.Equal(expected.NameBytes, actual.NameBytes.ToArray());
            Assert.Equal(expected.ObfuscatedNameBytes, actual.ObfuscatedNameBytes.ToArray());
            Assert.Equal(expected.EncodedNameSize, actual.EncodedNameSize);
            Assert.Equal(ypf.EffectiveNameXor, actual.NameXor);
            Assert.Equal((byte)expected.FileType, actual.FileType);
            Assert.Equal(expected.SizeStored, actual.SizeStored);
            Assert.Equal(expected.Offset, actual.Offset);
            Assert.Equal(expected.DataHash, actual.DataHash);
            Assert.Equal(Map(ypf.OffsetSize), actual.OffsetSize);
            Assert.Equal(expected.IndexPosition, actual.IndexPosition);
            Assert.Equal(expected.IndexEntrySize, actual.IndexEntrySize);
            Assert.Equal(Map(ypf.SwapTable), actual.SwapTable);
        }
    }

    public static TheoryData<uint, SyntheticYpfOffsetSize, SyntheticYpfHashAlgorithm, SyntheticYpfHashAlgorithm> Variants =>
        SyntheticYpfSamples.Variants;

    public static TheoryData<uint, SyntheticYpfOffsetSize, SyntheticYpfHashAlgorithm, SyntheticYpfHashAlgorithm> CoreVariants =>
        SyntheticYpfSamples.CoreVariants;

    // ---- Variantes do gerador ----

    // ---- Chave, tabela, hashes e header_size detectados ----

    [Fact]
    public void Open_ChaveSemPontoNaExtensaoCaiNasChavesConhecidasEDepoisNaVarredura()
    {
        // Nomes sem '.' na posição len-4: a dedução do GARbro erra e o hash decide a chave.
        var ypf = new SyntheticYpfBuilder(300)
            .WithNameKey(0x2B)
            .AddFile("abcdefgh", [1])
            .AddFile("zyxwvuts", [2])
            .AddFile("naotemponto", [3])
            .Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal((byte)0x2B, archive.Parameters.NameKey);
    }

    [Theory]
    [InlineData(200u, true)]
    [InlineData(300u, false)]
    [InlineData(200u, false)]
    [InlineData(300u, true)]
    public void Open_AceitaAsDuasSemanticasDoHeaderSize(uint version, bool includesHeader)
    {
        var ypf = SyntheticYpfSamples.DefaultSample(version).WithHeaderSizeIncludesHeader(includesHeader).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal(includesHeader, archive.Parameters.HeaderSizeIncludesHeader);
    }

    [Fact]
    public void Open_NomeEmShiftJisEhDecodificado()
    {
        var ypf = new SyntheticYpfBuilder(300).AddFile(@"ysbin\第一章\表.ybn", [1]).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal("ysbin/第一章/表.ybn", archive.Entries.Single().Path);
    }
}
