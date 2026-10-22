using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

public class SyntheticYpfBuilderTests
{
    private static readonly byte[] SmallBinary = [0x10, 0x20, 0x30, 0x40, 0x50];
    private static readonly byte[] Repetitive = Enumerable.Repeat((byte)'A', 4000).ToArray();

    public static TheoryData<uint, SyntheticYpfOffsetSize> Variants => new()
    {
        { 224, SyntheticYpfOffsetSize.Bits32 },
        { 264, SyntheticYpfOffsetSize.Bits32 },
        { 265, SyntheticYpfOffsetSize.Bits32 },
        { 300, SyntheticYpfOffsetSize.Bits32 },
        { 478, SyntheticYpfOffsetSize.Bits32 },
        { 479, SyntheticYpfOffsetSize.Bits64 },
        { 479, SyntheticYpfOffsetSize.Bits32 },
        { 500, SyntheticYpfOffsetSize.Bits64 },
        { 500, SyntheticYpfOffsetSize.Bits32 },
    };

    private static SyntheticYpfBuilder SampleBuilder(uint version, SyntheticYpfOffsetSize offsetSize) =>
        new SyntheticYpfBuilder(version)
            .WithOffsetSize(offsetSize)
            .AddFile("ysbin/yst00001.ybn", SmallBinary, SyntheticYpfFileType.Ybn)
            .AddFile("bg/fundo.png", Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile("se/porta.ogg", [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile("ysbin/yst00002.ybn", Repetitive, SyntheticYpfFileType.Ybn, compress: true)
            .AddFile("ysbin/vazio.ybn", [], SyntheticYpfFileType.Ybn);

    private static int OffsetBytes(SyntheticYpfOffsetSize size) => size == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;

    private static byte[] Decompress(byte[] stored)
    {
        using var input = new MemoryStream(stored);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static uint Hash(SyntheticYpfHashAlgorithm algorithm, byte[] data) => algorithm switch
    {
        SyntheticYpfHashAlgorithm.Adler32 => YpfHash.Adler32(data),
        SyntheticYpfHashAlgorithm.Crc32 => YpfHash.Crc32(data),
        SyntheticYpfHashAlgorithm.MurmurHash2 => YpfHash.MurmurHash2(data),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };

    // Tabela do doc 06, escrita aqui de novo de propósito: o teste não pode depender da tabela do gerador.
    private static byte DocSwap(byte[] table, byte value)
    {
        for (var i = 0; i < table.Length; i += 2)
        {
            if (value == table[i]) return table[i + 1];
            if (value == table[i + 1]) return table[i];
        }

        return value;
    }

    private static readonly byte[] DocStandardTable = [3, 72, 6, 53, 9, 11, 12, 16, 13, 19, 17, 25, 21, 27, 28, 30, 32, 35, 38, 41, 44, 47, 46, 50];
    private static readonly byte[] DocV500Table = [3, 10, 6, 53, 9, 11, 12, 16, 13, 19, 17, 24, 20, 46, 21, 27, 28, 30, 32, 35, 38, 41, 44, 47];

    [Theory]
    [MemberData(nameof(Variants))]
    public void Cabecalho_TemMagicVersaoContagemTamanhoEPadding(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();
        var bytes = ypf.Bytes;

        Assert.Equal("YPF\0"u8.ToArray(), bytes[..4]);
        Assert.Equal(version, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(5u, ypf.FileCount);
        Assert.Equal(ypf.HeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        Assert.All(bytes[16..32], b => Assert.Equal(0, b));

        var indexSize = ypf.Entries.Sum(e => e.IndexEntrySize);
        Assert.Equal((uint)(SyntheticYpf.FixedHeaderSize + indexSize), ypf.HeaderSize);
        Assert.Equal(ypf.HeaderSize + (uint)ypf.Entries.Sum(e => (long)e.SizeStored), (uint)bytes.Length);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Indice_TemOLayoutDoDoc06EOsCamposDoDescritor(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();
        var bytes = ypf.Bytes;
        var position = SyntheticYpf.FixedHeaderSize;

        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(position, entry.IndexPosition);
            var span = bytes.AsSpan(position);

            Assert.Equal(entry.NameHash, BinaryPrimitives.ReadUInt32LittleEndian(span));
            Assert.Equal(entry.EncodedNameSize, span[4]);
            var nameSize = entry.ObfuscatedNameBytes.Length;
            Assert.Equal(entry.ObfuscatedNameBytes, span.Slice(5, nameSize).ToArray());

            var cursor = 5 + nameSize;
            Assert.Equal((byte)entry.FileType, span[cursor]);
            Assert.Equal(entry.IsCompressed ? 1 : 0, span[cursor + 1]);
            Assert.Equal(entry.SizeUncompressed, BinaryPrimitives.ReadUInt32LittleEndian(span[(cursor + 2)..]));
            Assert.Equal(entry.SizeStored, BinaryPrimitives.ReadUInt32LittleEndian(span[(cursor + 6)..]));

            cursor += 10;
            ulong offset = offsetSize == SyntheticYpfOffsetSize.Bits64
                ? BinaryPrimitives.ReadUInt64LittleEndian(span[cursor..])
                : BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            Assert.Equal(entry.Offset, offset);

            cursor += OffsetBytes(offsetSize);
            Assert.Equal(entry.DataHash, BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]));

            Assert.Equal(cursor + 4, entry.IndexEntrySize);
            position += entry.IndexEntrySize;
        }

        Assert.Equal((int)ypf.HeaderSize, position);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Dados_FicamAposOIndiceNaOrdemDeInsercaoEBatemComOsOffsets(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();
        var insertionOrder = new[] { "ysbin/yst00001.ybn", "bg/fundo.png", "se/porta.ogg", "ysbin/yst00002.ybn", "ysbin/vazio.ybn" };

        var expectedOffset = (ulong)ypf.HeaderSize;
        foreach (var name in insertionOrder)
        {
            var entry = ypf.Entries.Single(e => e.Name == name);
            Assert.Equal(expectedOffset, entry.Offset);
            Assert.Equal(entry.StoredBytes, ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]);
            expectedOffset += entry.SizeStored;
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Entradas_FicamOrdenadasPeloHashDoNome(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();

        var hashes = ypf.Entries.Select(e => e.NameHash).ToArray();
        Assert.Equal(hashes.Order().ToArray(), hashes);
    }

    [Theory]
    [InlineData(100u, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfOffsetSize.Bits32, SyntheticYpfSwapTable.Standard)]
    [InlineData(264u, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfOffsetSize.Bits32, SyntheticYpfSwapTable.Standard)]
    [InlineData(265u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfOffsetSize.Bits32, SyntheticYpfSwapTable.Standard)]
    [InlineData(478u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Adler32, SyntheticYpfOffsetSize.Bits32, SyntheticYpfSwapTable.Standard)]
    [InlineData(479u, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfOffsetSize.Bits64, SyntheticYpfSwapTable.Standard)]
    [InlineData(500u, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfOffsetSize.Bits64, SyntheticYpfSwapTable.V500)]
    public void Padroes_SeguemATabelaDeVersoesDoDoc06(
        uint version,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfSwapTable swapTable)
    {
        var ypf = new SyntheticYpfBuilder(version).AddFile("a.ybn", SmallBinary).Build();

        Assert.Equal(nameHash, ypf.NameHashAlgorithm);
        Assert.Equal(dataHash, ypf.DataHashAlgorithm);
        Assert.Equal(offsetSize, ypf.OffsetSize);
        Assert.Equal(swapTable, ypf.SwapTable);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Hashes_BatemComYpfHash(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();

        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(Hash(ypf.NameHashAlgorithm, entry.NameBytes), entry.NameHash);
            Assert.Equal(Hash(ypf.DataHashAlgorithm, entry.StoredBytes), entry.DataHash);
        }
    }

    [Fact]
    public void Hashes_SaoSobreOsBytesArmazenadosEntradaComprimida()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile("a.ybn", Repetitive, compress: true).Build();
        var entry = ypf.Entries.Single();

        Assert.NotEqual(YpfHash.MurmurHash2(Repetitive), entry.DataHash);
        Assert.Equal(YpfHash.MurmurHash2(entry.StoredBytes), entry.DataHash);
    }

    [Theory]
    [InlineData(478u, SyntheticYpfHashAlgorithm.MurmurHash2, SyntheticYpfHashAlgorithm.MurmurHash2)]
    [InlineData(479u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Adler32)]
    [InlineData(100u, SyntheticYpfHashAlgorithm.Crc32, SyntheticYpfHashAlgorithm.Crc32)]
    public void Hashes_AlgoritmosPodemSerSobrescritos(uint version, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = new SyntheticYpfBuilder(version)
            .WithNameHash(nameHash)
            .WithDataHash(dataHash)
            .AddFile("ysbin/a.ybn", SmallBinary)
            .AddFile("ysbin/b.ybn", Repetitive, compress: true)
            .Build();

        Assert.Equal(nameHash, ypf.NameHashAlgorithm);
        Assert.Equal(dataHash, ypf.DataHashAlgorithm);
        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(Hash(nameHash, entry.NameBytes), entry.NameHash);
            Assert.Equal(Hash(dataHash, entry.StoredBytes), entry.DataHash);
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x40)]
    [InlineData(0xFF)]
    [InlineData(0x5A)]
    public void Nomes_VoltamAoOriginalComAChaveEATabelaPadrao(int key)
    {
        var ypf = new SyntheticYpfBuilder(300)
            .WithNameKey((byte)key)
            .AddFile("ysbin/yst00001.ybn", SmallBinary)
            .AddFile("bg/cena_01.png", SmallBinary, SyntheticYpfFileType.Png)
            .AddFile("a.b", SmallBinary)
            .Build();

        Assert.Equal((byte)key, ypf.NameKey);
        foreach (var entry in ypf.Entries)
        {
            var length = DocSwap(DocStandardTable, (byte)~entry.EncodedNameSize);
            Assert.Equal(entry.Name.Length, length);

            var plain = entry.ObfuscatedNameBytes.Select(b => (byte)(b ^ key)).ToArray();
            Assert.Equal(length, plain.Length);
            Assert.Equal(entry.Name, Encoding.ASCII.GetString(plain));
        }
    }

    [Fact]
    public void Nomes_ChaveDiferenteDeZeroMudaOsBytesGravados()
    {
        var plain = new SyntheticYpfBuilder(300).WithNameKey(0).AddFile("abc.ybn", SmallBinary).Build();
        var keyed = new SyntheticYpfBuilder(300).WithNameKey(0x40).AddFile("abc.ybn", SmallBinary).Build();

        Assert.Equal("abc.ybn"u8.ToArray(), plain.Entries.Single().ObfuscatedNameBytes);
        Assert.Equal("abc.ybn"u8.ToArray().Select(b => (byte)(b ^ 0x40)).ToArray(), keyed.Entries.Single().ObfuscatedNameBytes);
        Assert.Equal(plain.Entries.Single().NameHash, keyed.Entries.Single().NameHash);
    }

    [Theory]
    [InlineData(SyntheticYpfSwapTable.Standard)]
    [InlineData(SyntheticYpfSwapTable.V500)]
    public void TabelaDeTroca_AplicaCadaParNosDoisSentidos(SyntheticYpfSwapTable table)
    {
        var doc = table == SyntheticYpfSwapTable.Standard ? DocStandardTable : DocV500Table;
        var builder = new SyntheticYpfBuilder(300).WithSwapTable(table);
        var lengths = doc.Select(b => (int)b).Concat(new[] { 1, 2, 5, 100, 255 }).Distinct().ToArray();
        foreach (var length in lengths)
        {
            builder.AddFile(new string('n', length), SmallBinary);
        }

        var ypf = builder.Build();

        Assert.Equal(lengths.Length, ypf.Entries.Count);
        foreach (var entry in ypf.Entries)
        {
            var length = (byte)entry.NameBytes.Length;
            Assert.Equal((byte)~DocSwap(doc, length), entry.EncodedNameSize);
            Assert.Equal(length, DocSwap(doc, (byte)~entry.EncodedNameSize));
        }
    }

    [Fact]
    public void TabelaDeTroca_V500DifereDaPadraoNosTamanhosEsperados()
    {
        var standard = new SyntheticYpfBuilder(300).AddFile("a.b", SmallBinary).Build().Entries.Single();
        var v500 = new SyntheticYpfBuilder(300).WithSwapTable(SyntheticYpfSwapTable.V500).AddFile("a.b", SmallBinary).Build().Entries.Single();

        Assert.Equal(0xB7, standard.EncodedNameSize); // ~72
        Assert.Equal(0xF5, v500.EncodedNameSize); // ~10
    }

    [Fact]
    public void Nomes_ComJapones_UsamBytesShiftJisENaoCaracteres()
    {
        const string name = "シナリオ/第一章.ybn";
        var expected = GameEncodings.ShiftJis.GetBytes(name);
        var ypf = new SyntheticYpfBuilder(500).WithNameKey(0x40).AddFile(name, SmallBinary).Build();
        var entry = ypf.Entries.Single();

        Assert.Equal(expected, entry.NameBytes);
        Assert.NotEqual(name.Length, entry.NameBytes.Length);
        Assert.Equal(YpfHash.MurmurHash2(expected), entry.NameHash);
        Assert.Equal((byte)~DocSwap(DocV500Table, (byte)expected.Length), entry.EncodedNameSize);
        Assert.Equal(expected, entry.ObfuscatedNameBytes.Select(b => (byte)(b ^ 0x40)).ToArray());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Zlib_DescomprimeParaOConteudoOriginal(uint version, SyntheticYpfOffsetSize offsetSize)
    {
        var ypf = SampleBuilder(version, offsetSize).Build();

        var compressed = ypf.Entries.Where(e => e.IsCompressed).ToArray();
        Assert.Equal(2, compressed.Length);
        foreach (var entry in compressed)
        {
            var stored = ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)];
            Assert.Equal(entry.Content, Decompress(stored));
            Assert.Equal((uint)entry.Content.Length, entry.SizeUncompressed);
            Assert.True(entry.SizeStored < entry.SizeUncompressed);
        }
    }

    [Fact]
    public void EntradasSemZlib_GuardamOConteudoCruEIgualamOsTamanhos()
    {
        var ypf = SampleBuilder(500, SyntheticYpfOffsetSize.Bits64).Build();

        foreach (var entry in ypf.Entries.Where(e => !e.IsCompressed))
        {
            Assert.Equal(entry.Content, entry.StoredBytes);
            Assert.Equal(entry.SizeUncompressed, entry.SizeStored);
            Assert.Equal(entry.Content, ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]);
        }
    }

    [Fact]
    public void TiposDeArquivo_SaoGravadosComoPedidos()
    {
        var builder = new SyntheticYpfBuilder(500);
        foreach (var type in Enum.GetValues<SyntheticYpfFileType>())
        {
            builder.AddFile($"arquivo_{(byte)type}.dat", SmallBinary, type);
        }

        var ypf = builder.Build();

        Assert.Equal(10, ypf.Entries.Count);
        foreach (var entry in ypf.Entries)
        {
            Assert.Equal($"arquivo_{(byte)entry.FileType}.dat", entry.Name);
            var span = ypf.Bytes.AsSpan(entry.IndexPosition + 5 + entry.ObfuscatedNameBytes.Length);
            Assert.Equal((byte)entry.FileType, span[0]);
        }
    }

    [Fact]
    public void Build_EDeterministico()
    {
        var first = SampleBuilder(500, SyntheticYpfOffsetSize.Bits64).Build();
        var second = SampleBuilder(500, SyntheticYpfOffsetSize.Bits64).Build();

        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact]
    public void Build_PodeSerChamadoMaisDeUmaVez()
    {
        var builder = SampleBuilder(300, SyntheticYpfOffsetSize.Bits32);

        Assert.Equal(builder.Build().Bytes, builder.Build().Bytes);
    }

    [Fact]
    public void ArquivoVazio_TemSoOCabecalho()
    {
        var ypf = new SyntheticYpfBuilder(500).Build();

        Assert.Equal(SyntheticYpf.FixedHeaderSize, ypf.Bytes.Length);
        Assert.Equal((uint)SyntheticYpf.FixedHeaderSize, ypf.HeaderSize);
        Assert.Equal(0u, ypf.FileCount);
        Assert.Empty(ypf.Entries);
    }

    [Fact]
    public void AddFile_RejeitaNomeDuplicado()
    {
        var builder = new SyntheticYpfBuilder(500).AddFile("a.ybn", SmallBinary);

        Assert.Throws<ArgumentException>(() => builder.AddFile("a.ybn", SmallBinary));
    }

    [Fact]
    public void AddFile_RejeitaNomeVazioOuMaiorQue255Bytes()
    {
        var builder = new SyntheticYpfBuilder(500);

        Assert.Throws<ArgumentException>(() => builder.AddFile("", SmallBinary));
        Assert.Throws<ArgumentException>(() => builder.AddFile(new string('x', 256), SmallBinary));
        builder.AddFile(new string('x', 255), SmallBinary);
        Assert.Equal(255, builder.Build().Entries.Single().NameBytes.Length);
    }
}
