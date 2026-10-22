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

    // Os três vetores do SyntheticYpfBuilderTests foram montados fora do gerador: uma entrada "bg\scene_0001.png", conteúdo "ABC".
    private static void AssertGolden(
        string hex,
        uint version,
        uint headerSize,
        bool includesHeader,
        YpfHashAlgorithm nameHash,
        YpfHashAlgorithm dataHash,
        YpfOffsetSize offsetSize,
        YpfSwapTable swapTable,
        byte nameXor,
        uint expectedNameHash,
        uint expectedDataHash,
        ulong expectedOffset)
    {
        using var archive = Open(Convert.FromHexString(hex));

        var entry = Assert.Single(archive.Entries);
        Assert.Equal("bg/scene_0001.png", entry.Path);
        Assert.Equal(3, entry.Size);
        Assert.False(entry.IsCompressed);
        Assert.Equal((byte)2, entry.FileType);
        Assert.Equal(3u, entry.SizeStored);
        Assert.Equal(expectedOffset, entry.Offset);
        Assert.Equal(expectedNameHash, entry.NameHash);
        Assert.Equal(expectedDataHash, entry.DataHash);
        Assert.Equal(new YpfHeader(version, 1, headerSize), archive.Header);
        Assert.Equal(includesHeader, archive.Parameters.HeaderSizeIncludesHeader);
        Assert.Equal(nameHash, archive.Parameters.NameHash);
        Assert.Equal(dataHash, archive.Parameters.DataHash);
        Assert.Equal(offsetSize, archive.Parameters.OffsetSize);
        Assert.Equal(nameXor, archive.Parameters.NameXor);
        Assert.Equal(swapTable, entry.SwapTable);
    }

    [Fact]
    public void Open_VetorDeOuroV200()
    {
        AssertGolden(
            SyntheticYpfBuilderTests.GoldenV200, 200, 0x28, false,
            YpfHashAlgorithm.Adler32, YpfHashAlgorithm.Adler32, YpfOffsetSize.Bits32, YpfSwapTable.Standard,
            0xFF, 0x36A705C7, 0x018D00C7, 0x48);
    }

    [Fact]
    public void Open_VetorDeOuroV300()
    {
        AssertGolden(
            SyntheticYpfBuilderTests.GoldenV300, 300, 0x48, true,
            YpfHashAlgorithm.Crc32, YpfHashAlgorithm.Adler32, YpfOffsetSize.Bits32, YpfSwapTable.Standard,
            0xFF, 0xE582ABD0, 0x018D00C7, 0x48);
    }

    [Fact]
    public void Open_VetorDeOuroV500()
    {
        AssertGolden(
            SyntheticYpfBuilderTests.GoldenV500, 500, 0x4C, true,
            YpfHashAlgorithm.MurmurHash2, YpfHashAlgorithm.MurmurHash2, YpfOffsetSize.Bits64, YpfSwapTable.V500,
            0xC9, 0xE099F84B, 0xFD17A8FF, 0x4C);
    }

    // ---- Erros ----

    private static InvalidFileException AssertInvalid(byte[] bytes, params string[] expectedFragments)
    {
        var ex = Assert.Throws<InvalidFileException>(() => Open(bytes));
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, ex.Message);
        }

        return ex;
    }

    [Theory]
    [InlineData(100u)]
    [InlineData(199u)]
    [InlineData(1000u)]
    [InlineData(9999u)]
    public void Open_VersaoDesconhecidaLancaUnknownVersionException(uint version)
    {
        var ypf = new SyntheticYpfBuilder(version).AddFile("a.ybn", [1]).Build();

        var ex = Assert.Throws<UnknownVersionException>(() => Open(ypf.Bytes));

        Assert.Equal(version.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Version);
        Assert.Contains("Versão desconhecida", ex.Message);
    }

    [Fact]
    public void Open_SemAssinaturaYpfLancaErroQueDizOQueFalhou()
    {
        var bytes = new byte[64];
        "ZIP\0"u8.CopyTo(bytes);

        AssertInvalid(bytes, "assinatura", "não é um arquivo YPF");
    }

    [Fact]
    public void Open_ArquivoMenorQueOCabecalhoLancaErro()
    {
        AssertInvalid("YPF\0"u8.ToArray(), "4 bytes", "32");
    }

    [Fact]
    public void Open_ContagemDeArquivosMaiorQueOArquivoLancaErro()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = (byte[])ypf.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 100_000);

        AssertInvalid(bytes, "100000 arquivos");
    }

    [Fact]
    public void Open_ContagemMenorQueONumeroRealDeEntradasDizOQueEsperavaEOQueAchou()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = (byte[])ypf.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), ypf.FileCount - 1);

        AssertInvalid(bytes, "header_size");
    }

    [Theory]
    [InlineData(200u)]
    [InlineData(300u)]
    [InlineData(500u)]
    public void Open_HashDoNomeErradoLancaErroComAEntradaEAPosicao(uint version)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).Build();
        var target = ypf.Entries[2];

        AssertInvalid(
            ypf.WithWrongNameHash(target.Name),
            "hash do nome",
            "entrada 3 de 5",
            $"posição {target.IndexPosition}");
    }

    [Theory]
    [InlineData(200u, 1)]
    [InlineData(300u, 1)]
    [InlineData(300u, -1)]
    [InlineData(500u, 32)]
    [InlineData(500u, 0)]
    public void Open_HeaderSizeInconsistenteDizOEsperadoEOGravado(uint version, int delta)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).Build();
        var wrong = delta == 0 ? 7u : (uint)(ypf.HeaderSize + delta);
        var indexSize = (int)ypf.DataStart - YpfHeader.Size;

        AssertInvalid(
            ypf.WithHeaderSize(wrong),
            $"header_size gravado é {wrong}",
            $"ocupa {indexSize} bytes",
            $"esperado era {indexSize} (só o índice) ou {ypf.DataStart} (índice e os 32 bytes do cabeçalho)");
    }

    [Theory]
    [InlineData(300u)]
    [InlineData(500u)]
    public void Open_ArquivoTruncadoNosDadosLancaErroComOTamanhoDoArquivo(uint version)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(version).Build();
        var truncated = ypf.Truncated(1);

        AssertInvalid(truncated, "mas o arquivo tem só", $"{truncated.Length} bytes", "truncado");
    }

    [Fact]
    public void Open_ArquivoTruncadoNoIndiceLancaErro()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var truncated = ypf.Bytes[..((int)ypf.DataStart - 10)];

        AssertInvalid(truncated, "não foi possível ler o índice");
    }

    [Fact]
    public void Open_NomesQueDecodificamParaOMesmoCaminhoLancamErro()
    {
        var ypf = new SyntheticYpfBuilder(300)
            .AddFile(@"bg\fundo.png", [1])
            .AddFile("bg/fundo.png", [2])
            .Build();

        AssertInvalid(ypf.Bytes, "bg/fundo.png", "mais de uma entrada");
    }

    [Fact]
    public void Open_HashDosDadosErradoEmArquivoDeUmaEntradaLancaErro()
    {
        var ypf = new SyntheticYpfBuilder(300).AddFile(@"a\um.ybn", [1, 2, 3, 4]).Build();

        AssertInvalid(ypf.WithWrongDataHash(@"a\um.ybn"), "hash dos dados", "Adler32", "a/um.ybn");
    }

    [Fact]
    public void Open_DadoOuHashCorrompidoNumaEntradaSoApareceAoAbriLaQuandoOutrasConfirmamOAlgoritmo()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();

        using var wrongHash = Open(ypf.WithWrongDataHash(SyntheticYpfBuilderTests.Script1));
        using var corrupted = Open(ypf.WithCorruptedData(SyntheticYpfBuilderTests.Script1));

        Assert.Equal(YpfHashAlgorithm.Adler32, wrongHash.Parameters.DataHash);
        Assert.Equal(YpfHashAlgorithm.Adler32, corrupted.Parameters.DataHash);
        Assert.Equal(ypf.Entries.Count, corrupted.Entries.Count);
    }

    // ---- CanOpen e ciclo de vida ----

    [Fact]
    public void CanOpen_ReconheceYpfEDevolveAPosicaoDoFluxo()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var stream = new MemoryStream(ypf.Bytes) { Position = 11 };

        Assert.True(Format.CanOpen(stream));
        Assert.Equal(11, stream.Position);
    }

    [Fact]
    public void CanOpen_RecusaOutroFormatoEFluxoCurto()
    {
        Assert.False(Format.CanOpen(new MemoryStream("PK\u0003\u0004"u8.ToArray())));
        Assert.False(Format.CanOpen(new MemoryStream(new byte[64])));
        Assert.False(Format.CanOpen(new MemoryStream("YPF\0"u8.ToArray())));
    }

    [Fact]
    public void CanOpen_AceitaVersaoDesconhecidaParaOpenExplicarOErro()
    {
        var ypf = new SyntheticYpfBuilder(9999).Build();

        Assert.True(Format.CanOpen(new MemoryStream(ypf.Bytes)));
    }

    [Fact]
    public void Formato_TemIdEExtensao()
    {
        Assert.Equal("ypf", Format.Id);
        Assert.Equal([".ypf"], Format.FileExtensions);
    }

    [Fact]
    public void Open_ErroNaoFechaOFluxoDeQuemChamou()
    {
        using var stream = new MemoryStream(new byte[64]);

        Assert.Throws<InvalidFileException>(() => Format.Open(stream));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Open_LeAPartirDoInicioMesmoComOFluxoNoMeio()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var stream = new MemoryStream(ypf.Bytes) { Position = ypf.Bytes.Length - 3 };

        using var archive = (YpfArchive)Format.Open(stream);

        AssertMatches(ypf, archive);
    }

    [Fact]
    public void Dispose_FechaOFluxoDeOrigem()
    {
        var stream = new MemoryStream(SyntheticYpfBuilderTests.DefaultSample(300).Build().Bytes);
        var archive = Format.Open(stream);

        archive.Dispose();
        archive.Dispose();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void OpenEntry_AindaNaoImplementadoCitaAIssue28()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        var ex = Assert.Throws<NotSupportedException>(() => archive.OpenEntry(archive.Entries[0]));

        Assert.Contains("#28", ex.Message);
    }

    [Fact]
    public void OpenEntry_RejeitaEntradaDeOutroPacoteENula()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var other = Open(ypf.Bytes);

        Assert.Throws<ArgumentException>(() => archive.OpenEntry(other.Entries[0]));
        Assert.Throws<ArgumentException>(() => archive.OpenEntry(new ArchiveEntry("x", 1, false)));
        Assert.Throws<ArgumentNullException>(() => archive.OpenEntry(null!));
    }

    [Fact]
    public void WriteTo_AindaNaoImplementadoCitaAIssue29()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        var ex = Assert.Throws<NotSupportedException>(() => archive.WriteTo(new MemoryStream(), new Dictionary<string, byte[]>()));

        Assert.Contains("#29", ex.Message);
    }

    [Fact]
    public void OpenEntry_DepoisDoDisposeLancaObjectDisposed()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var archive = Open(ypf.Bytes);
        var entry = archive.Entries[0];
        archive.Dispose();

        Assert.Throws<ObjectDisposedException>(() => archive.OpenEntry(entry));
    }

    // ---- Tabelas de troca e hash ----

    [Fact]
    public void SwapTables_BatemComATabelaDoGerador()
    {
        foreach (var table in Enum.GetValues<SyntheticYpfSwapTable>())
        {
            for (var value = 0; value < 256; value++)
            {
                Assert.Equal(
                    SyntheticYpfSwapTables.Swap(table, (byte)value),
                    YpfSwapTables.Swap(Map(table), (byte)value));
            }
        }
    }

    [Fact]
    public void YpfHashCompute_UsaOAlgoritmoEscolhido()
    {
        var data = "ABC"u8.ToArray();

        Assert.Equal(YpfHash.Adler32(data), YpfHash.Compute(YpfHashAlgorithm.Adler32, data));
        Assert.Equal(YpfHash.Crc32(data), YpfHash.Compute(YpfHashAlgorithm.Crc32, data));
        Assert.Equal(YpfHash.MurmurHash2(data), YpfHash.Compute(YpfHashAlgorithm.MurmurHash2, data));
    }
}
