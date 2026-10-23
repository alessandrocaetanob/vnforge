using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

public class SyntheticYpfBuilderTests
{
    private const string AYbn = "a.ybn";
    private const string SmallYbn = "pequeno.ybn";
    private const string Hex03000000 = "03000000";

    internal const string Script1 = @"ysbin\yst00001.ybn";
    internal const string Image = @"bg\fundo.png";
    internal const string Sound = @"se\porta.ogg";
    internal const string Script2 = @"ysbin\yst00002.ybn";
    internal const string Empty = @"ysbin\vazio.ybn";

    internal static readonly byte[] SmallBinary = [0x10, 0x20, 0x30, 0x40, 0x50];
    internal static readonly byte[] Repetitive = Enumerable.Repeat((byte)'A', 4000).ToArray();

    private static readonly string[] InsertionOrder = [Script1, Image, Sound, Script2, Empty];

    private const SyntheticYpfHashAlgorithm A32 = SyntheticYpfHashAlgorithm.Adler32;
    private const SyntheticYpfHashAlgorithm Crc = SyntheticYpfHashAlgorithm.Crc32;
    private const SyntheticYpfHashAlgorithm Mur = SyntheticYpfHashAlgorithm.MurmurHash2;
    private const SyntheticYpfOffsetSize Off32 = SyntheticYpfOffsetSize.Bits32;
    private const SyntheticYpfOffsetSize Off64 = SyntheticYpfOffsetSize.Bits64;

    // Versão, offset, hash do nome e hash dos dados, todos explícitos. As linhas 477 e 478 cobrem as duas leituras do limiar
    // do MurmurHash2 (docs/06): a antiga (CRC32, Adler32, 32 bits) e a nova (MurmurHash2, MurmurHash2, 64 bits).
    public static TheoryData<uint, SyntheticYpfOffsetSize, SyntheticYpfHashAlgorithm, SyntheticYpfHashAlgorithm> Variants => new()
    {
        { 200, Off32, A32, A32 },
        { 264, Off32, A32, A32 },
        { 265, Off32, Crc, A32 },
        { 290, Off32, Crc, A32 },
        { 300, Off32, Crc, A32 },
        { 474, Off32, Crc, A32 },
        { 477, Off32, Crc, A32 },
        { 477, Off64, Mur, Mur },
        { 478, Off32, Crc, A32 },
        { 478, Off64, Mur, Mur },
        { 479, Off64, Mur, Mur },
        { 479, Off32, Mur, Mur },
        { 490, Off64, Mur, Mur },
        { 500, Off64, Mur, Mur },
        { 500, Off32, Mur, Mur },
    };

    internal static SyntheticYpfBuilder SampleBuilder(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash) =>
        new SyntheticYpfBuilder(version)
            .WithOffsetSize(offsetSize)
            .WithNameHash(nameHash)
            .WithDataHash(dataHash)
            .AddFile(Script1, SmallBinary, SyntheticYpfFileType.Ybn)
            .AddFile(Image, Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile(Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile(Script2, Repetitive, SyntheticYpfFileType.Ybn, compress: true)
            .AddFile(Empty, [], SyntheticYpfFileType.Ybn);

    internal static SyntheticYpfBuilder DefaultSample(uint version) =>
        new SyntheticYpfBuilder(version)
            .AddFile(Script1, SmallBinary)
            .AddFile(Image, Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile(Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile(Script2, Repetitive, compress: true)
            .AddFile(Empty, []);

    private static int OffsetBytes(SyntheticYpfOffsetSize size) => size == Off64 ? 8 : 4;

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
        A32 => YpfHash.Adler32(data),
        Crc => YpfHash.Crc32(data),
        Mur => YpfHash.MurmurHash2(data),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };

    // As tabelas abaixo são o doc 06 e as fontes (yuri.py:39-41, GARbro ArcYPF.cs:530-541) escritas de novo de
    // propósito: o teste não pode depender das tabelas do gerador.
    private static readonly byte[] DocStandardTable = [3, 72, 6, 53, 9, 11, 12, 16, 13, 19, 17, 25, 21, 27, 28, 30, 32, 35, 38, 41, 44, 47, 46, 50];
    private static readonly byte[] DocV500Table = [3, 10, 6, 53, 9, 11, 12, 16, 13, 19, 17, 24, 20, 46, 21, 27, 28, 30, 32, 35, 38, 41, 44, 47];
    private static readonly byte[] DocGarbro04Table = [0x0C, 0x10, 0x11, 0x19, 0x1C, 0x1E, 0x09, 0x0B, 0x0D, 0x13, 0x15, 0x1B, 0x20, 0x23, 0x26, 0x29, 0x2C, 0x2F, 0x2E, 0x32];
    private static readonly byte[] DocGarbro10Table = [0x09, 0x0B, 0x0D, 0x13, 0x15, 0x1B, 0x20, 0x23, 0x26, 0x29, 0x2C, 0x2F, 0x2E, 0x32];

    private static byte[] DocTable(SyntheticYpfSwapTable table) => table switch
    {
        SyntheticYpfSwapTable.Standard => DocStandardTable,
        SyntheticYpfSwapTable.V500 => DocV500Table,
        SyntheticYpfSwapTable.Garbro04 => DocGarbro04Table,
        SyntheticYpfSwapTable.Garbro10 => DocGarbro10Table,
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private static byte DocSwap(byte[] table, byte value)
    {
        for (var i = 0; i < table.Length; i += 2)
        {
            if (value == table[i]) return table[i + 1];
            if (value == table[i + 1]) return table[i];
        }

        return value;
    }

    // ---- Cabeçalho, índice e dados ----

    [Theory]
    [MemberData(nameof(Variants))]
    public void Cabecalho_TemMagicVersaoContagemTamanhoEPadding(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();
        var bytes = ypf.Bytes;

        Assert.Equal("YPF\0"u8.ToArray(), bytes[..4]);
        Assert.Equal(version, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal(5u, ypf.FileCount);
        Assert.Equal(ypf.HeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
        Assert.All(bytes[16..32], b => Assert.Equal(0, b));

        var indexSize = ypf.Entries.Sum(e => e.IndexEntrySize);
        Assert.Equal((uint)(SyntheticYpf.FixedHeaderSize + indexSize), ypf.DataStart);
        Assert.Equal(version >= 300 ? ypf.DataStart : (uint)indexSize, ypf.HeaderSize);
        Assert.Equal(ypf.DataStart + (uint)ypf.Entries.Sum(e => (long)e.SizeStored), (uint)bytes.Length);
    }

    [Theory]
    [InlineData(200u, null, false)]
    [InlineData(264u, null, false)]
    [InlineData(299u, null, false)]
    [InlineData(300u, null, true)]
    [InlineData(500u, null, true)]
    [InlineData(200u, true, true)]
    [InlineData(299u, true, true)]
    [InlineData(300u, false, false)]
    [InlineData(500u, false, false)]
    public void HeaderSize_IncluiOCabecalhoSoDaV300EPodeSerForcado(uint version, bool? forced, bool expectedIncludes)
    {
        var ypf = DefaultSample(version).WithHeaderSizeIncludesHeader(forced).Build();

        var indexSize = (uint)ypf.Entries.Sum(e => e.IndexEntrySize);
        Assert.Equal(expectedIncludes, ypf.HeaderSizeIncludesHeader);
        Assert.Equal(expectedIncludes ? indexSize + 0x20 : indexSize, ypf.HeaderSize);
        Assert.Equal(ypf.HeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(ypf.Bytes.AsSpan(12)));
        // O início dos dados não depende da semântica do campo.
        Assert.Equal(indexSize + 0x20, ypf.DataStart);
        Assert.Equal(ypf.DataStart, (uint)ypf.Entries.Min(e => (long)e.Offset));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Indice_TemOLayoutDoDoc06EOsCamposDoDescritor(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();
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
            ulong offset = offsetSize == Off64
                ? BinaryPrimitives.ReadUInt64LittleEndian(span[cursor..])
                : BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            Assert.Equal(entry.Offset, offset);

            cursor += OffsetBytes(offsetSize);
            Assert.Equal(entry.DataHash, BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]));

            Assert.Equal(cursor + 4, entry.IndexEntrySize);
            position += entry.IndexEntrySize;
        }

        Assert.Equal((int)ypf.DataStart, position);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Dados_NaOrdemDoIndiceFicamSeguidosAposOIndice(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

        var expectedOffset = (ulong)ypf.DataStart;
        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(expectedOffset, entry.Offset);
            Assert.Equal(entry.StoredBytes, ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]);
            expectedOffset += entry.SizeStored;
        }
    }

    [Fact]
    public void Dados_NaOrdemDeInsercaoFicamSeguidosEOsOffsetsDoIndiceNaoCrescem()
    {
        var ypf = DefaultSample(500).WithDataOrder(SyntheticYpfDataOrder.Insertion).Build();

        var expectedOffset = (ulong)ypf.DataStart;
        foreach (var name in InsertionOrder)
        {
            var entry = ypf.Entries.Single(e => e.Name == name);
            Assert.Equal(expectedOffset, entry.Offset);
            Assert.Equal(entry.StoredBytes, ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]);
            expectedOffset += entry.SizeStored;
        }

        var offsets = ypf.Entries.Select(e => e.Offset).ToArray();
        Assert.NotEqual(offsets.Order().ToArray(), offsets);
        Assert.Equal(expectedOffset, (ulong)ypf.Bytes.Length);
    }

    [Fact]
    public void Dados_AsDuasOrdensGuardamOsMesmosBytesEmLugaresDiferentes()
    {
        var byIndex = DefaultSample(500).Build();
        var byInsertion = DefaultSample(500).WithDataOrder(SyntheticYpfDataOrder.Insertion).Build();

        Assert.Equal(byIndex.Bytes.Length, byInsertion.Bytes.Length);
        Assert.Equal(byIndex.DataStart, byInsertion.DataStart);
        Assert.NotEqual(byIndex.Bytes, byInsertion.Bytes);
        foreach (var entry in byInsertion.Entries)
        {
            var other = byIndex.Entries.Single(e => e.Name == entry.Name);
            Assert.Equal(other.StoredBytes, entry.StoredBytes);
            Assert.Equal(other.DataHash, entry.DataHash);
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Entradas_FicamOrdenadasPeloHashDoNome(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

        var hashes = ypf.Entries.Select(e => e.NameHash).ToArray();
        Assert.Equal(hashes.Order().ToArray(), hashes);
    }

    // ---- Padrões por versão e hashes ----

    [Theory]
    [InlineData(100u, A32, A32, Off32, SyntheticYpfSwapTable.Standard)]
    [InlineData(264u, A32, A32, Off32, SyntheticYpfSwapTable.Standard)]
    [InlineData(265u, Crc, A32, Off32, SyntheticYpfSwapTable.Standard)]
    [InlineData(478u, Crc, A32, Off32, SyntheticYpfSwapTable.Standard)]
    [InlineData(479u, Mur, Mur, Off64, SyntheticYpfSwapTable.Standard)]
    [InlineData(499u, Mur, Mur, Off64, SyntheticYpfSwapTable.Standard)]
    [InlineData(500u, Mur, Mur, Off64, SyntheticYpfSwapTable.V500)]
    [InlineData(501u, Mur, Mur, Off64, SyntheticYpfSwapTable.Standard)]
    public void Padroes_SeguemATabelaDeVersoesDoDoc06(
        uint version,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfSwapTable swapTable)
    {
        var ypf = new SyntheticYpfBuilder(version).AddFile(AYbn, SmallBinary).Build();

        Assert.Equal(nameHash, ypf.NameHashAlgorithm);
        Assert.Equal(dataHash, ypf.DataHashAlgorithm);
        Assert.Equal(offsetSize, ypf.OffsetSize);
        Assert.Equal(swapTable, ypf.SwapTable);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Hashes_BatemComYpfHash(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

        Assert.Equal(nameHash, ypf.NameHashAlgorithm);
        Assert.Equal(dataHash, ypf.DataHashAlgorithm);
        Assert.Equal(offsetSize, ypf.OffsetSize);
        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(Hash(nameHash, entry.NameBytes), entry.NameHash);
            Assert.Equal(Hash(dataHash, entry.StoredBytes), entry.DataHash);
        }
    }

    [Fact]
    public void Hashes_SaoSobreOsBytesArmazenadosEntradaComprimida()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile(AYbn, Repetitive, compress: true).Build();
        var entry = ypf.Entries.Single();

        Assert.True(entry.IsCompressed);
        Assert.NotEqual(YpfHash.MurmurHash2(Repetitive), entry.DataHash);
        Assert.Equal(YpfHash.MurmurHash2(entry.StoredBytes), entry.DataHash);
    }

    [Theory]
    [InlineData(478u, Mur, Mur)]
    [InlineData(479u, Crc, A32)]
    [InlineData(100u, Crc, Crc)]
    public void Hashes_AlgoritmosPodemSerSobrescritos(uint version, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = new SyntheticYpfBuilder(version)
            .WithNameHash(nameHash)
            .WithDataHash(dataHash)
            .AddFile(@"ysbin\a.ybn", SmallBinary)
            .AddFile(@"ysbin\b.ybn", Repetitive, compress: true)
            .Build();

        Assert.Equal(nameHash, ypf.NameHashAlgorithm);
        Assert.Equal(dataHash, ypf.DataHashAlgorithm);
        foreach (var entry in ypf.Entries)
        {
            Assert.Equal(Hash(nameHash, entry.NameBytes), entry.NameHash);
            Assert.Equal(Hash(dataHash, entry.StoredBytes), entry.DataHash);
        }
    }

    [Fact]
    public void HashDoNome_EDoOffset_SaoIndependentesDosHashesDosDados()
    {
        var ypf = new SyntheticYpfBuilder(477)
            .WithNameHash(Mur)
            .WithDataHash(A32)
            .WithOffsetSize(Off64)
            .AddFile(Script1, SmallBinary)
            .Build();

        Assert.Equal(Mur, ypf.NameHashAlgorithm);
        Assert.Equal(A32, ypf.DataHashAlgorithm);
        Assert.Equal(Off64, ypf.OffsetSize);
        Assert.Equal(YpfHash.MurmurHash2(ypf.Entries.Single().NameBytes), ypf.Entries.Single().NameHash);
        Assert.Equal(YpfHash.Adler32(SmallBinary), ypf.Entries.Single().DataHash);
    }

    // ---- Nome: chave K, tabela de troca, codificação ----

    [Theory]
    [InlineData(100u, 0x00, 0xFF)]
    [InlineData(289u, 0x00, 0xFF)]
    [InlineData(290u, 0x40, 0xBF)]
    [InlineData(291u, 0x00, 0xFF)]
    [InlineData(300u, 0x00, 0xFF)]
    [InlineData(499u, 0x00, 0xFF)]
    [InlineData(500u, 0x36, 0xC9)]
    [InlineData(501u, 0x00, 0xFF)]
    public void ChaveDoNome_PadraoDeCadaFamiliaEGravadaComoComplemento(uint version, int expectedKey, int expectedEffectiveXor)
    {
        var ypf = DefaultSample(version).Build();

        Assert.Equal((byte)expectedKey, ypf.NameKey);
        Assert.Equal((byte)expectedEffectiveXor, ypf.EffectiveNameXor);
        foreach (var entry in ypf.Entries)
        {
            // Fórmula do YPF-Manager (YPFArchive.cs:219): gravado = ~(byte ^ K).
            var expected = entry.NameBytes.Select(b => (byte)(0xFF - (b ^ expectedKey))).ToArray();
            Assert.Equal(expected, entry.ObfuscatedNameBytes);
            Assert.Equal(entry.NameBytes, entry.ObfuscatedNameBytes.Select(b => (byte)(b ^ expectedEffectiveXor)).ToArray());
        }
    }

    [Theory]
    [InlineData(100u, null)]
    [InlineData(290u, null)]
    [InlineData(500u, null)]
    [InlineData(300u, 0x5A)]
    [InlineData(500u, 0x00)]
    [InlineData(100u, 0xFF)]
    public void ChaveDoNome_ComoOGarbroRecupera_PeloPontoDaExtensao(uint version, int? overrideKey)
    {
        var builder = DefaultSample(version);
        if (overrideKey is { } key)
        {
            builder.WithNameKey((byte)key);
        }

        var ypf = builder.Build();

        foreach (var entry in ypf.Entries)
        {
            // GARbro ArcYPF.cs:473: a chave efetiva é o byte gravado na posição len-4 XOR '.' (extensão de 3 letras).
            var raw = entry.ObfuscatedNameBytes;
            var guessed = (byte)(raw[^4] ^ '.');
            Assert.Equal(ypf.EffectiveNameXor, guessed);
            Assert.Equal(entry.NameBytes, raw.Select(b => (byte)(b ^ guessed)).ToArray());
        }
    }

    [Fact]
    public void ChaveDoNome_WithNameKeySobrescreveAChaveDaVersao()
    {
        var ypf = new SyntheticYpfBuilder(500).WithNameKey(0x00).AddFile("abc.ybn", SmallBinary).Build();
        var entry = ypf.Entries.Single();

        Assert.Equal((byte)0x00, ypf.NameKey);
        Assert.Equal("abc.ybn"u8.ToArray().Select(b => (byte)~b).ToArray(), entry.ObfuscatedNameBytes);
        Assert.Equal(YpfHash.MurmurHash2("abc.ybn"u8), entry.NameHash);
    }

    [Fact]
    public void Nomes_ChavesDiferentesMudamOsBytesGravadosMasNaoOHash()
    {
        var a = new SyntheticYpfBuilder(300).WithNameKey(0x00).AddFile("abc.ybn", SmallBinary).Build().Entries.Single();
        var b = new SyntheticYpfBuilder(300).WithNameKey(0x40).AddFile("abc.ybn", SmallBinary).Build().Entries.Single();

        Assert.NotEqual(a.ObfuscatedNameBytes, b.ObfuscatedNameBytes);
        Assert.Equal(a.NameHash, b.NameHash);
    }

    [Theory]
    [InlineData(SyntheticYpfSwapTable.Standard)]
    [InlineData(SyntheticYpfSwapTable.V500)]
    [InlineData(SyntheticYpfSwapTable.Garbro04)]
    [InlineData(SyntheticYpfSwapTable.Garbro10)]
    public void TabelaDeTroca_AplicaCadaParNosDoisSentidos(SyntheticYpfSwapTable table)
    {
        var doc = DocTable(table);
        var builder = new SyntheticYpfBuilder(300).WithSwapTable(table);
        var lengths = doc.Select(b => (int)b).Concat(new[] { 1, 2, 3, 5, 100, 255 }).Distinct().ToArray();
        foreach (var length in lengths)
        {
            builder.AddFile(new string('n', length), SmallBinary);
        }

        var ypf = builder.Build();

        Assert.Equal(table, ypf.SwapTable);
        Assert.Equal(lengths.Length, ypf.Entries.Count);
        foreach (var entry in ypf.Entries)
        {
            var length = (byte)entry.NameBytes.Length;
            Assert.Equal((byte)~DocSwap(doc, length), entry.EncodedNameSize);
            Assert.Equal(length, DocSwap(doc, (byte)~entry.EncodedNameSize));
        }
    }

    [Theory]
    [InlineData(100u, SyntheticYpfSwapTable.Garbro04)]
    [InlineData(255u, SyntheticYpfSwapTable.Garbro04)]
    [InlineData(300u, SyntheticYpfSwapTable.Garbro10)]
    [InlineData(405u, SyntheticYpfSwapTable.Garbro10)]
    public void TabelaDeTroca_ReduzidasDoGarbroNosTamanhosQueElasDiferenciam(uint version, SyntheticYpfSwapTable table)
    {
        var doc = DocTable(table);
        var builder = new SyntheticYpfBuilder(version).WithSwapTable(table);
        foreach (var length in new[] { 3, 6, 12, 17, 28 })
        {
            builder.AddFile(new string('n', length), SmallBinary);
        }

        var ypf = builder.Build();

        foreach (var entry in ypf.Entries)
        {
            var length = (byte)entry.NameBytes.Length;
            Assert.Equal((byte)~DocSwap(doc, length), entry.EncodedNameSize);
            // Tamanhos 3 e 6 não trocam em nenhuma das duas; 12, 17 e 28 trocam só na Garbro04.
            var swapped = DocSwap(doc, length) != length;
            Assert.Equal(table == SyntheticYpfSwapTable.Garbro04 && length is 12 or 17 or 28, swapped);
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
        const string name = "シナリオ\\第一章.ybn";
        var expected = GameEncodings.ShiftJis.GetBytes(name);
        var ypf = new SyntheticYpfBuilder(500).AddFile(name, SmallBinary).Build();
        var entry = ypf.Entries.Single();

        Assert.Equal(expected, entry.NameBytes);
        Assert.NotEqual(name.Length, entry.NameBytes.Length);
        Assert.Equal(YpfHash.MurmurHash2(expected), entry.NameHash);
        Assert.Equal((byte)~DocSwap(DocV500Table, (byte)expected.Length), entry.EncodedNameSize);
        Assert.Equal(expected, entry.ObfuscatedNameBytes.Select(b => (byte)(b ^ 0xC9)).ToArray());
    }

    [Fact]
    public void Nomes_AceitamBarraInvertidaENormalSemConverter()
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(@"bg\a.png", SmallBinary)
            .AddFile("bg/a.png", SmallBinary)
            .Build();

        Assert.Equal(2, ypf.Entries.Count);
        Assert.Contains(ypf.Entries, e => e.NameBytes.SequenceEqual("bg\\a.png"u8.ToArray()));
        Assert.Contains(ypf.Entries, e => e.NameBytes.SequenceEqual("bg/a.png"u8.ToArray()));
    }

    // ---- Conteúdo: zlib, dedup, tipos ----

    [Theory]
    [MemberData(nameof(Variants))]
    public void Zlib_DescomprimeParaOConteudoOriginal(uint version, SyntheticYpfOffsetSize offsetSize, SyntheticYpfHashAlgorithm nameHash, SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SampleBuilder(version, offsetSize, nameHash, dataHash).Build();

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
    public void Zlib_SoComprimeQuandoOResultadoFicaMenor()
    {
        var ypf = new SyntheticYpfBuilder(500)
            .AddFile(SmallYbn, SmallBinary, compress: true)
            .AddFile("vazio.ybn", [], compress: true)
            .AddFile("grande.ybn", Repetitive, compress: true)
            .Build();

        Assert.False(ypf.Entries.Single(e => e.Name == SmallYbn).IsCompressed);
        Assert.False(ypf.Entries.Single(e => e.Name == "vazio.ybn").IsCompressed);
        Assert.True(ypf.Entries.Single(e => e.Name == "grande.ybn").IsCompressed);
        Assert.Equal(SmallBinary, ypf.Entries.Single(e => e.Name == SmallYbn).StoredBytes);
    }

    [Fact]
    public void Zlib_ForceCompressionGravaOFluxoMesmoQuandoNaoEncolhe()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile(SmallYbn, SmallBinary, forceCompression: true).Build();
        var entry = ypf.Entries.Single();

        Assert.True(entry.IsCompressed);
        Assert.True(entry.SizeStored > entry.SizeUncompressed);
        Assert.Equal(SmallBinary, Decompress(entry.StoredBytes));
        Assert.Equal(YpfHash.MurmurHash2(entry.StoredBytes), entry.DataHash);
        Assert.Equal(1, ypf.Bytes[entry.IndexPosition + 5 + entry.ObfuscatedNameBytes.Length + 1]);
    }

    [Fact]
    public void EntradasSemZlib_GuardamOConteudoCruEIgualamOsTamanhos()
    {
        var ypf = SampleBuilder(500, Off64, Mur, Mur).Build();

        foreach (var entry in ypf.Entries.Where(e => !e.IsCompressed))
        {
            Assert.Equal(entry.Content, entry.StoredBytes);
            Assert.Equal(entry.SizeUncompressed, entry.SizeStored);
            Assert.Equal(entry.Content, ypf.Bytes[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]);
        }
    }

    [Fact]
    public void DadosDuplicados_PorPadraoSaoGravadosDuasVezes()
    {
        var ypf = DuplicateSample().Build();

        var a = ypf.Entries.Single(e => e.Name == AYbn);
        var b = ypf.Entries.Single(e => e.Name == "b.ybn");
        Assert.NotEqual(a.Offset, b.Offset);
        Assert.Equal(ypf.DataStart + (uint)ypf.Entries.Sum(e => (long)e.SizeStored), (uint)ypf.Bytes.Length);
    }

    [Fact]
    public void DadosDuplicados_ComCompartilhamentoApontamParaOMesmoOffset()
    {
        var separate = DuplicateSample().Build();
        var shared = DuplicateSample().WithSharedDuplicateData().Build();

        var a = shared.Entries.Single(e => e.Name == AYbn);
        var b = shared.Entries.Single(e => e.Name == "b.ybn");
        var raw = shared.Entries.Single(e => e.Name == "c.ybn");
        var other = shared.Entries.Single(e => e.Name == "d.ybn");

        Assert.Equal(a.Offset, b.Offset);
        Assert.Equal(a.StoredBytes, shared.Bytes[(int)b.Offset..(int)(b.Offset + b.SizeStored)]);
        Assert.Equal(a.DataHash, b.DataHash);
        // Mesmo conteúdo cru, mas sem zlib: bytes armazenados diferentes, então não compartilha.
        Assert.NotEqual(a.Offset, raw.Offset);
        Assert.NotEqual(a.Offset, other.Offset);
        Assert.Equal(separate.Bytes.Length - a.SizeStored, (uint)shared.Bytes.Length);
        Assert.Equal(shared.DataStart, (uint)shared.Entries.Min(e => (long)e.Offset));
    }

    private static SyntheticYpfBuilder DuplicateSample() =>
        new SyntheticYpfBuilder(500)
            .AddFile(AYbn, Repetitive, compress: true)
            .AddFile("b.ybn", Repetitive, compress: true)
            .AddFile("c.ybn", Repetitive)
            .AddFile("d.ybn", SmallBinary);

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

    // ---- Determinismo e entradas inválidas ----

    [Fact]
    public void Build_EDeterministico()
    {
        var first = SampleBuilder(500, Off64, Mur, Mur).Build();
        var second = SampleBuilder(500, Off64, Mur, Mur).Build();

        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact]
    public void Build_PodeSerChamadoMaisDeUmaVez()
    {
        var builder = DefaultSample(300);

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
        var builder = new SyntheticYpfBuilder(500).AddFile(AYbn, SmallBinary);

        Assert.Throws<ArgumentException>(() => builder.AddFile(AYbn, SmallBinary));
    }

    [Fact]
    public void AddFile_RejeitaCaractereForaDoShiftJis()
    {
        var builder = new SyntheticYpfBuilder(500);

        Assert.Throws<ArgumentException>(() => builder.AddFile("foo😀.ybn", SmallBinary));
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

    // ---- Defeitos pontuais para testes negativos ----

    private static int CountDifferences(byte[] a, byte[] b) => a.Zip(b).Count(p => p.First != p.Second);

    [Fact]
    public void Mutacoes_HashErradoDoNomeMudaSoOCampoDaEntrada()
    {
        var ypf = DefaultSample(500).Build();
        var entry = ypf.Entries.Single(e => e.Name == Sound);

        var mutated = ypf.WithWrongNameHash(Sound);

        Assert.Equal(1, CountDifferences(ypf.Bytes, mutated));
        Assert.NotEqual(entry.NameHash, BinaryPrimitives.ReadUInt32LittleEndian(mutated.AsSpan(entry.IndexPosition)));
    }

    [Fact]
    public void Mutacoes_HashErradoDosDadosMudaSoOCampoDaEntrada()
    {
        var ypf = DefaultSample(500).Build();
        var entry = ypf.Entries.Single(e => e.Name == Sound);

        var mutated = ypf.WithWrongDataHash(Sound);

        Assert.Equal(1, CountDifferences(ypf.Bytes, mutated));
        var position = entry.IndexPosition + entry.IndexEntrySize - 4;
        Assert.NotEqual(entry.DataHash, BinaryPrimitives.ReadUInt32LittleEndian(mutated.AsSpan(position)));
    }

    [Fact]
    public void Mutacoes_DadosCorrompidosDeixamOIndiceIntacto()
    {
        var ypf = DefaultSample(500).Build();
        var entry = ypf.Entries.Single(e => e.Name == Sound);

        var mutated = ypf.WithCorruptedData(Sound);

        Assert.Equal(1, CountDifferences(ypf.Bytes, mutated));
        Assert.NotEqual(Hash(ypf.DataHashAlgorithm, mutated[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)]), entry.DataHash);
        Assert.Throws<InvalidOperationException>(() => ypf.WithCorruptedData(Empty));
    }

    [Fact]
    public void Mutacoes_HeaderSizeInconsistenteEDadosTruncados()
    {
        var ypf = DefaultSample(500).Build();

        var wrongHeader = ypf.WithHeaderSize(ypf.HeaderSize + 1);
        Assert.Equal(ypf.HeaderSize + 1, BinaryPrimitives.ReadUInt32LittleEndian(wrongHeader.AsSpan(12)));
        Assert.Equal(ypf.Bytes.Length, wrongHeader.Length);

        var truncated = ypf.Truncated(3);
        Assert.Equal(ypf.Bytes.Length - 3, truncated.Length);
        Assert.Equal(ypf.Bytes[..^3], truncated);
        Assert.Throws<ArgumentException>(() => ypf.WithWrongNameHash("nao-existe"));
    }

    // ---- Vetores de ouro, montados fora do gerador ----

    // Cada vetor foi montado com um script Python independente (zlib.crc32, zlib.adler32 e um MurmurHash2 escrito
    // à parte), seguindo yuri.md:440-457 e o layout de ypf.py:121-145. Uma entrada, conteúdo "ABC" sem zlib, tipo png (2),
    // nome "bg\scene_0001.png" (17 bytes: o tamanho passa pela tabela de troca).
    //
    //   0x00  "YPF\0" · versão · 1 arquivo · header_size · 16 bytes zero
    //   0x20  name_hash · name_size · nome · tipo · compressed · size · stored · offset · data_hash
    //   fim   "ABC"
    internal const string GoldenV200 =
        "595046" + "00" + "c8000000" + "01000000" + "28000000" + "00000000000000000000000000000000" +
        "c705a736" + "e6" + "9d98a38c9c9a919aa0cfcfcfced18f9198" + "02" + "00" + Hex03000000 + Hex03000000 + "48000000" + "c7008d01" +
        "414243";

    internal const string GoldenV300 =
        "595046" + "00" + "2c010000" + "01000000" + "48000000" + "00000000000000000000000000000000" +
        "d0ab82e5" + "e6" + "9d98a38c9c9a919aa0cfcfcfced18f9198" + "02" + "00" + Hex03000000 + Hex03000000 + "48000000" + "c7008d01" +
        "414243";

    internal const string GoldenV500 =
        "595046" + "00" + "f4010000" + "01000000" + "4c000000" + "00000000000000000000000000000000" +
        "4bf899e0" + "e7" + "abae95baaaaca7ac96f9f9f9f8e7b9a7ae" + "02" + "00" + Hex03000000 + Hex03000000 + "4c00000000000000" + "ffa817fd" +
        "414243";

    [Theory]
    [InlineData(200u, GoldenV200)]
    [InlineData(300u, GoldenV300)]
    [InlineData(500u, GoldenV500)]
    public void VetorDeOuro_UmaEntradaPorFamiliaBateComOsBytesMontadosAMao(uint version, string expectedHex)
    {
        var ypf = new SyntheticYpfBuilder(version)
            .AddFile(@"bg\scene_0001.png", "ABC"u8.ToArray(), SyntheticYpfFileType.Png)
            .Build();

        Assert.Equal(expectedHex, Convert.ToHexStringLower(ypf.Bytes));
    }

    [Fact]
    public void VetorDeOuro_DescritorBateComOsCamposDoVetorV500()
    {
        var ypf = new SyntheticYpfBuilder(500).AddFile(@"bg\scene_0001.png", "ABC"u8.ToArray(), SyntheticYpfFileType.Png).Build();
        var entry = ypf.Entries.Single();

        Assert.Equal(0xE099F84Bu, entry.NameHash);
        Assert.Equal(0xFD17A8FFu, entry.DataHash);
        Assert.Equal((byte)0xE7, entry.EncodedNameSize);
        Assert.Equal(0x4Cul, entry.Offset);
        Assert.Equal(0x1F4u, BinaryPrimitives.ReadUInt32LittleEndian(ypf.Bytes.AsSpan(4)));
    }
}
