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
        SyntheticYpfBuilderTests.Variants;

    // ---- Variantes do gerador ----

    [Theory]
    [MemberData(nameof(Variants))]
    public void Open_LeOIndiceDeCadaVarianteDoGerador(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SyntheticYpfBuilderTests.SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
    }

    [Theory]
    [InlineData(200u)]
    [InlineData(290u)]
    [InlineData(300u)]
    [InlineData(479u)]
    [InlineData(500u)]
    public void Open_VersoesComOsPadroesDoGerador(uint version)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
    }

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index)]
    [InlineData(SyntheticYpfDataOrder.Insertion)]
    public void Open_DadosEmOrdemDeIndiceOuDeInsercao(SyntheticYpfDataOrder order)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithDataOrder(order).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
    }

    [Fact]
    public void Open_DadosCompartilhadosApontamParaOMesmoOffset()
    {
        var ypf = new SyntheticYpfBuilder(300)
            .WithSharedDuplicateData()
            .AddFile(@"a\um.ybn", SyntheticYpfBuilderTests.SmallBinary)
            .AddFile(@"a\dois.ybn", SyntheticYpfBuilderTests.SmallBinary)
            .AddFile(@"a\tres.ybn", [9, 9, 9])
            .Build();
        Assert.Equal(2, ypf.Entries.Select(e => e.Offset).Distinct().Count());

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal(2, archive.Entries.Select(e => e.Offset).Distinct().Count());
    }

    [Fact]
    public void Open_SeparadoresBarraInvertidaEBarraNormalViramBarraNoCaminho()
    {
        var ypf = new SyntheticYpfBuilder(300)
            .AddFile(@"bg\fundo.png", [1, 2, 3])
            .AddFile("se/porta.ogg", [4, 5, 6])
            .Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Contains(archive.Entries, e => e.Path == "bg/fundo.png");
        Assert.Contains(archive.Entries, e => e.Path == "se/porta.ogg");
        Assert.Contains(archive.Entries, e => e.NameBytes.Span.SequenceEqual("bg\\fundo.png"u8));
    }

    [Fact]
    public void Open_NomeEmShiftJisEhDecodificado()
    {
        var ypf = new SyntheticYpfBuilder(300).AddFile(@"ysbin\第一章\表.ybn", [1]).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal("ysbin/第一章/表.ybn", archive.Entries.Single().Path);
    }

    [Fact]
    public void Open_ArquivoSemEntradasEhValido()
    {
        var ypf = new SyntheticYpfBuilder(500).Build();

        using var archive = Open(ypf.Bytes);

        Assert.Empty(archive.Entries);
        Assert.Equal(YpfHeader.Size, archive.Parameters.DataStart);
        Assert.Equal(YpfVersionDefaults.NameXor(500), archive.Parameters.NameXor);
    }

    // ---- Chave, tabela, hashes e header_size detectados ----

    [Theory]
    [InlineData(300u, 0x5A)]
    [InlineData(300u, 0x00)]
    [InlineData(300u, 0xFF)]
    [InlineData(290u, 0x00)]
    [InlineData(500u, 0x00)]
    [InlineData(500u, 0x77)]
    public void Open_DetectaChaveDoNomeDiferenteDaDaVersao(uint version, int key)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).WithNameKey((byte)key).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal((byte)key, archive.Parameters.NameKey);
    }

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

    // Um nome de cada tamanho que as tabelas tratam de forma diferente: 3, 6, 10, 12, 17, 20, 24, 28, 46, 53, 72.
    private static readonly int[] DiscriminatingLengths = [3, 6, 10, 12, 17, 20, 24, 28, 46, 53, 72];

    private static SyntheticYpfBuilder DiscriminatingNames(uint version, SyntheticYpfSwapTable table)
    {
        var builder = new SyntheticYpfBuilder(version).WithSwapTable(table);
        for (var i = 0; i < DiscriminatingLengths.Length; i++)
        {
            var length = DiscriminatingLengths[i];
            var name = length >= 5
                ? (char)('a' + i) + new string('n', length - 5) + ".ybn"
                : "a.b"[..length];
            Assert.Equal(length, name.Length);
            builder.AddFile(name, [(byte)i]);
        }

        return builder;
    }

    [Theory]
    [InlineData(200u, SyntheticYpfSwapTable.Standard)]
    [InlineData(200u, SyntheticYpfSwapTable.Garbro04)]
    [InlineData(300u, SyntheticYpfSwapTable.Standard)]
    [InlineData(300u, SyntheticYpfSwapTable.Garbro10)]
    [InlineData(300u, SyntheticYpfSwapTable.V500)]
    [InlineData(300u, SyntheticYpfSwapTable.Garbro04)]
    [InlineData(500u, SyntheticYpfSwapTable.V500)]
    [InlineData(500u, SyntheticYpfSwapTable.Standard)]
    [InlineData(501u, SyntheticYpfSwapTable.V500)]
    [InlineData(501u, SyntheticYpfSwapTable.Garbro10)]
    public void Open_DetectaATabelaDeTrocaPeloHashDoNome(uint version, SyntheticYpfSwapTable table)
    {
        var ypf = DiscriminatingNames(version, table).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal(Map(table), archive.Parameters.SwapTable);
        Assert.All(archive.Entries, e => Assert.Equal(Map(table), e.SwapTable));
    }

    [Theory]
    [InlineData(200u, true)]
    [InlineData(299u, true)]
    [InlineData(300u, false)]
    [InlineData(500u, false)]
    [InlineData(200u, false)]
    [InlineData(300u, true)]
    public void Open_AceitaAsDuasSemanticasDoHeaderSize(uint version, bool includesHeader)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).WithHeaderSizeIncludesHeader(includesHeader).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
        Assert.Equal(includesHeader, archive.Parameters.HeaderSizeIncludesHeader);
    }

    [Theory]
    [InlineData(300u, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfOffsetSize.Bits64)]
    [InlineData(300u, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfOffsetSize.Bits32)]
    [InlineData(500u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfOffsetSize.Bits32)]
    [InlineData(200u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfOffsetSize.Bits64)]
    public void Open_DetectaOsAlgoritmosPelaComparacaoENaoPelaVersao(
        uint version,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash,
        SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SyntheticYpfBuilderTests.SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
    }

    [Fact]
    public void Open_ArquivoSoComEntradasVaziasUsaOHashDeDadosPadraoDaVersao()
    {
        // Murmur e CRC32 de dados vazios valem 0: a amostra não separa os dois e vale o padrão da versão.
        var ypf = new SyntheticYpfBuilder(500).AddFile(@"a\vazio.ybn", []).Build();

        using var archive = Open(ypf.Bytes);

        Assert.Single(archive.Entries);
        Assert.Equal(YpfHashAlgorithm.MurmurHash2, archive.Parameters.DataHash);
        Assert.False(archive.Parameters.DataHashVerified);
    }
}
