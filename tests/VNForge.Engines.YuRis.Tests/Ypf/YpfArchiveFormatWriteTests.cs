using System.IO.Compression;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;
using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    // ---- Gravação com reescrita mínima (#29) ----

    private static readonly byte[] NewImage = Enumerable.Repeat((byte)'B', 6000).ToArray();
    private static readonly byte[] NewScript2 = Enumerable.Repeat((byte)'C', 5000).ToArray();
    private static readonly byte[] NewScript1 = Enumerable.Range(100, 20).Select(i => (byte)i).ToArray();
    private static readonly byte[] NewSound = [9, 9];

    private static string Key(string name) => name.Replace('\\', '/');

    private static byte[] Write(YpfArchive archive, IReadOnlyDictionary<string, byte[]>? replacements = null)
    {
        using var output = new MemoryStream();
        archive.WriteTo(output, replacements ?? new Dictionary<string, byte[]>());
        return output.ToArray();
    }

    private static Dictionary<string, byte[]> Replace(string name, byte[] content) => new() { [Key(name)] = content };

    /// <summary>A amostra de <see cref="SyntheticYpfBuilderTests.SampleBuilder"/> com o conteúdo de um arquivo trocado: o oráculo do escritor.</summary>
    private static SyntheticYpfBuilder Sample(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash,
        string? replaced = null,
        byte[]? content = null)
    {
        byte[] Pick(string name, byte[] original) => name == replaced ? content! : original;
        return new SyntheticYpfBuilder(version)
            .WithOffsetSize(offsetSize)
            .WithNameHash(nameHash)
            .WithDataHash(dataHash)
            .AddFile(SyntheticYpfBuilderTests.Script1, Pick(SyntheticYpfBuilderTests.Script1, SyntheticYpfBuilderTests.SmallBinary), SyntheticYpfFileType.Ybn)
            .AddFile(SyntheticYpfBuilderTests.Image, Pick(SyntheticYpfBuilderTests.Image, SyntheticYpfBuilderTests.Repetitive), SyntheticYpfFileType.Png, compress: true)
            .AddFile(SyntheticYpfBuilderTests.Sound, Pick(SyntheticYpfBuilderTests.Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9]), SyntheticYpfFileType.Ogg)
            .AddFile(SyntheticYpfBuilderTests.Script2, Pick(SyntheticYpfBuilderTests.Script2, SyntheticYpfBuilderTests.Repetitive), SyntheticYpfFileType.Ybn, compress: true)
            .AddFile(SyntheticYpfBuilderTests.Empty, [], SyntheticYpfFileType.Ybn);
    }

    private static readonly (string Name, byte[] Content)[] OracleCases =
    [
        (SyntheticYpfBuilderTests.Image, NewImage),
        (SyntheticYpfBuilderTests.Script2, NewScript2),
        (SyntheticYpfBuilderTests.Script1, NewScript1),
        (SyntheticYpfBuilderTests.Sound, NewSound),
    ];

    private static byte[] Stored(byte[] file, YpfEntry entry) => file[(int)entry.Offset..(int)(entry.Offset + entry.SizeStored)];

    // ---- Ida e volta sem mudança ----

    [Theory]
    [MemberData(nameof(Variants))]
    public void WriteTo_SemTrocasGeraArquivoIdenticoParaCadaVariante(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = Sample(version, offsetSize, nameHash, dataHash).Build();
        using var archive = Open(ypf.Bytes);

        Assert.Equal(ypf.Bytes, Write(archive));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void WriteTo_TrocarTudoPeloConteudoAtualGeraArquivoIdentico(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = Sample(version, offsetSize, nameHash, dataHash).Build();
        using var archive = Open(ypf.Bytes);
        var all = ypf.Entries.ToDictionary(e => Key(e.Name), e => e.Content);

        Assert.Equal(ypf.Bytes, Write(archive, all));
    }

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index, false)]
    [InlineData(SyntheticYpfDataOrder.Insertion, false)]
    [InlineData(SyntheticYpfDataOrder.Index, true)]
    [InlineData(SyntheticYpfDataOrder.Insertion, true)]
    public void WriteTo_SemTrocasGeraArquivoIdenticoComOrdemDeDadosEDuplicatas(SyntheticYpfDataOrder order, bool shared)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithDataOrder(order).WithSharedDuplicateData(shared).Build();
        using var archive = Open(ypf.Bytes);

        Assert.Equal(ypf.Bytes, Write(archive));
        Assert.Equal(ypf.Bytes, Write(archive, ypf.Entries.ToDictionary(e => Key(e.Name), e => e.Content)));
    }

    [Theory]
    [InlineData(SyntheticYpfHashAlgorithm.Adler32)]
    [InlineData(SyntheticYpfHashAlgorithm.Crc32)]
    [InlineData(SyntheticYpfHashAlgorithm.MurmurHash2)]
    public void WriteTo_SemTrocasGeraArquivoIdenticoParaCadaHashDeDados(SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithDataHash(dataHash).Build();
        using var archive = Open(ypf.Bytes);

        Assert.Equal(ypf.Bytes, Write(archive));
    }

    [Fact]
    public void WriteTo_PreservaPaddingLacunasETrailerNaIdaEVolta()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = WithGap(ypf, ypf.DataStart, 4);
        var second = ypf.Entries.Where(e => e.SizeStored > 0).OrderBy(e => e.Offset).Skip(1).First();
        var withMiddleGap = WithGap(ypf, (long)second.Offset, 3);
        foreach (var file in new[] { bytes, withMiddleGap })
        {
            var decorated = file.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();
            Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i)).ToArray().CopyTo(decorated, 16);
            using var archive = Open(decorated);

            Assert.Equal(decorated, Write(archive));
            Assert.Equal(decorated, Write(archive, ypf.Entries.ToDictionary(e => Key(e.Name), e => e.Content)));
        }
    }

    [Fact]
    public void WriteTo_PreservaXorDeNomePorEntradaNaIdaEVolta()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = WithEntryNameXor(ypf, ypf.Bytes, SyntheticYpfBuilderTests.Script2, 0x55);
        using var archive = Open(bytes);

        Assert.Equal(bytes, Write(archive));
    }

    [Fact]
    public void WriteTo_EntradaTrocadaPeloMesmoConteudoMantemOsBytesComprimidosDoOriginal()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithDataOrder(SyntheticYpfDataOrder.Insertion).Build();
        var script2 = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script2);
        var lastBlock = ypf.Entries.OrderBy(e => e.Offset).Last(e => e.SizeStored > 0);
        Assert.Equal(script2.Name, lastBlock.Name);

        // O original guarda o zlib sem compressão (mais longo que o do .NET), então só uma cópia crua o reproduz.
        var noCompression = Deflate(script2.Content, CompressionLevel.NoCompression);
        Assert.NotEqual(Deflate(script2.Content, CompressionLevel.Optimal), noCompression);
        var bytes = WithStoredBytes(ypf, script2, noCompression);
        using var archive = Open(bytes);

        Assert.Equal(bytes, Write(archive, Replace(script2.Name, script2.Content)));
    }

    private static byte[] Deflate(byte[] content, CompressionLevel level)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, level, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return buffer.ToArray();
    }

    /// <summary>Troca os bytes armazenados do último bloco de dados e acerta <c>size_stored</c>, <c>data_hash</c> e o offset de entradas vazias no fim.</summary>
    private static byte[] WithStoredBytes(SyntheticYpf ypf, SyntheticYpfEntry entry, byte[] stored)
    {
        var bytes = ypf.Bytes[..(int)entry.Offset].Concat(stored).ToArray();
        var offsetBytes = ypf.OffsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;
        var offsetPosition = OffsetFieldPosition(ypf, entry);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offsetPosition - 4), (uint)stored.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offsetPosition + offsetBytes),
            YpfHash.Compute(Map(ypf.DataHashAlgorithm), stored));
        foreach (var empty in ypf.Entries.Where(e => e.SizeStored == 0 && e.Offset == (ulong)ypf.Bytes.Length))
        {
            bytes = WithOffset(ypf, bytes, empty, (ulong)bytes.Length);
        }

        return bytes;
    }

    // ---- Troca de uma entrada ----

    [Theory]
    [MemberData(nameof(Variants))]
    public void WriteTo_ComUmaEntradaTrocadaGeraOMesmoArquivoQueOGeradorComOConteudoNovo(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var original = Sample(version, offsetSize, nameHash, dataHash).Build();
        using var archive = Open(original.Bytes);

        foreach (var (name, content) in OracleCases)
        {
            var expected = Sample(version, offsetSize, nameHash, dataHash, name, content).Build();

            Assert.Equal(expected.Bytes, Write(archive, Replace(name, content)));
        }
    }

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index)]
    [InlineData(SyntheticYpfDataOrder.Insertion)]
    public void WriteTo_ComUmaEntradaTrocadaMantemAOrdemDosDadosDoOriginal(SyntheticYpfDataOrder order)
    {
        var original = SyntheticYpfBuilderTests.DefaultSample(300).WithDataOrder(order).Build();
        using var archive = Open(original.Bytes);

        foreach (var (name, content) in new[] { (SyntheticYpfBuilderTests.Image, NewImage), (SyntheticYpfBuilderTests.Script1, NewScript1) })
        {
            var expected = new SyntheticYpfBuilder(300)
                .WithDataOrder(order)
                .AddFile(SyntheticYpfBuilderTests.Script1, name == SyntheticYpfBuilderTests.Script1 ? content : SyntheticYpfBuilderTests.SmallBinary)
                .AddFile(SyntheticYpfBuilderTests.Image, name == SyntheticYpfBuilderTests.Image ? content : SyntheticYpfBuilderTests.Repetitive, SyntheticYpfFileType.Png, compress: true)
                .AddFile(SyntheticYpfBuilderTests.Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
                .AddFile(SyntheticYpfBuilderTests.Script2, SyntheticYpfBuilderTests.Repetitive, compress: true)
                .AddFile(SyntheticYpfBuilderTests.Empty, [])
                .Build();

            Assert.Equal(expected.Bytes, Write(archive, Replace(name, content)));
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void WriteTo_ComUmaEntradaTrocadaAsOutrasSaemIdenticasETrocadaELidaDeVolta(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var original = Sample(version, offsetSize, nameHash, dataHash).Build();
        using var source = Open(original.Bytes);
        var output = Write(source, Replace(SyntheticYpfBuilderTests.Image, NewImage));

        using var rewritten = Open(output);

        Assert.True(rewritten.Parameters.DataHashVerified);
        Assert.Equal(source.Parameters.DataHash, rewritten.Parameters.DataHash);
        Assert.Equal(source.Header, rewritten.Header);
        Assert.Equal(source.Entries.Count, rewritten.Entries.Count);
        for (var i = 0; i < source.Entries.Count; i++)
        {
            var before = source.Entries[i];
            var after = rewritten.Entries[i];
            Assert.Equal(before.Path, after.Path);
            Assert.Equal(before.NameHash, after.NameHash);
            Assert.Equal(before.ObfuscatedNameBytes.ToArray(), after.ObfuscatedNameBytes.ToArray());
            Assert.Equal(before.EncodedNameSize, after.EncodedNameSize);
            Assert.Equal(before.FileType, after.FileType);
            Assert.Equal(before.IsCompressed, after.IsCompressed);
            Assert.Equal(before.IndexPosition, after.IndexPosition);

            using var content = rewritten.OpenEntry(after);
            if (before.Path == Key(SyntheticYpfBuilderTests.Image))
            {
                Assert.Equal(NewImage, ReadAll(content));
                Assert.Equal(NewImage.Length, after.Size);
                Assert.Equal(YpfHash.Compute(rewritten.Parameters.DataHash, Stored(output, after)), after.DataHash);
                continue;
            }

            Assert.Equal(before.Size, after.Size);
            Assert.Equal(before.SizeStored, after.SizeStored);
            Assert.Equal(before.DataHash, after.DataHash);
            Assert.Equal(Stored(original.Bytes, before), Stored(output, after));
            using var expected = source.OpenEntry(before);
            Assert.Equal(ReadAll(expected), ReadAll(content));
        }
    }

    [Fact]
    public void WriteTo_EntradaTrocadaMantemACompressaoDoOriginal()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        // Os 20 bytes de NewScript1 e o conteúdo de 6000 bytes do Image: a entrada não comprimida continua crua e a comprimida continua zlib.
        using var rewritten = Open(Write(archive, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfBuilderTests.Script1)] = NewScript1,
            [Key(SyntheticYpfBuilderTests.Image)] = [1, 2, 3],
        }));

        var script1 = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script1));
        var image = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Image));
        Assert.False(script1.IsCompressed);
        Assert.Equal((uint)NewScript1.Length, script1.SizeStored);
        Assert.True(image.IsCompressed);
        using var stream = rewritten.OpenEntry(image);
        Assert.Equal(new byte[] { 1, 2, 3 }, ReadAll(stream));
    }

    [Theory]
    [InlineData(SyntheticYpfHashAlgorithm.Adler32)]
    [InlineData(SyntheticYpfHashAlgorithm.Crc32)]
    [InlineData(SyntheticYpfHashAlgorithm.MurmurHash2)]
    public void WriteTo_RecalculaOHashDosDadosComOAlgoritmoDetectado(SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithDataHash(dataHash).Build();
        using var archive = Open(ypf.Bytes);
        var output = Write(archive, Replace(SyntheticYpfBuilderTests.Script1, NewScript1));

        using var rewritten = Open(output);
        var entry = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script1));

        Assert.Equal(Map(dataHash), rewritten.Parameters.DataHash);
        Assert.True(rewritten.Parameters.DataHashVerified);
        Assert.Equal(YpfHash.Compute(Map(dataHash), NewScript1), entry.DataHash);
    }

    [Fact]
    public void WriteTo_TrocaPorConteudoVazioELidaDeVolta()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        using var rewritten = Open(Write(archive, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfBuilderTests.Image)] = [],
            [Key(SyntheticYpfBuilderTests.Script1)] = [],
        }));

        foreach (var name in new[] { SyntheticYpfBuilderTests.Image, SyntheticYpfBuilderTests.Script1 })
        {
            using var stream = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(name)));
            Assert.Empty(ReadAll(stream));
        }

        using var sound = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Sound)));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, ReadAll(sound));
    }

    [Fact]
    public void WriteTo_TrocaDeEntradaVaziaPorConteudoELidaDeVolta()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        byte[] content = [4, 5, 6];

        var output = Write(archive, Replace(SyntheticYpfBuilderTests.Empty, content));
        using var rewritten = Open(output);

        Assert.Equal(archive.Entries.Count, rewritten.Entries.Count);
        foreach (var entry in rewritten.Entries)
        {
            using var stream = rewritten.OpenEntry(entry);
            var expected = entry.Path == Key(SyntheticYpfBuilderTests.Empty)
                ? content
                : ypf.Entries.Single(e => Key(e.Name) == entry.Path).Content;
            Assert.Equal(expected, ReadAll(stream));
        }
    }

    [Fact]
    public void WriteTo_TrocaDeVariasEntradasDeUmaVez()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(500).Build();
        using var archive = Open(ypf.Bytes);
        var replacements = new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfBuilderTests.Script1)] = NewScript1,
            [Key(SyntheticYpfBuilderTests.Image)] = NewImage,
            [Key(SyntheticYpfBuilderTests.Script2)] = NewScript2,
            [Key(SyntheticYpfBuilderTests.Empty)] = NewSound,
        };

        using var rewritten = Open(Write(archive, replacements));

        foreach (var entry in rewritten.Entries)
        {
            using var stream = rewritten.OpenEntry(entry);
            var expected = replacements.TryGetValue(entry.Path, out var replaced)
                ? replaced
                : ypf.Entries.Single(e => Key(e.Name) == entry.Path).Content;
            Assert.Equal(expected, ReadAll(stream));
        }
    }

    // ---- Dados compartilhados, lacunas e trailer ----

    [Fact]
    public void WriteTo_TrocarUmaEntradaDeDadosCompartilhadosPreservaAOutra()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithSharedDuplicateData().Build();
        var image = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Image);
        var script2 = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script2);
        Assert.Equal(image.Offset, script2.Offset);
        using var archive = Open(ypf.Bytes);

        var output = Write(archive, Replace(SyntheticYpfBuilderTests.Image, NewImage));
        using var rewritten = Open(output);

        using var newImage = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Image)));
        using var oldScript2 = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script2)));
        Assert.Equal(NewImage, ReadAll(newImage));
        Assert.Equal(SyntheticYpfBuilderTests.Repetitive, ReadAll(oldScript2));
        Assert.Equal(script2.Offset, rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script2)).Offset);
    }

    [Fact]
    public void WriteTo_TrocarTodasAsEntradasDeDadosCompartilhadosDescartaOBlocoAntigo()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).WithSharedDuplicateData().Build();
        var script2 = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script2);
        using var archive = Open(ypf.Bytes);

        var output = Write(archive, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfBuilderTests.Image)] = NewImage,
            [Key(SyntheticYpfBuilderTests.Script2)] = NewScript2,
        });
        using var rewritten = Open(output);

        var image = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Image));
        var script2After = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script2));
        Assert.Equal(ypf.Bytes.Length - (int)script2.SizeStored + (int)image.SizeStored + (int)script2After.SizeStored, output.Length);
        using var imageStream = rewritten.OpenEntry(image);
        using var script2Stream = rewritten.OpenEntry(script2After);
        Assert.Equal(NewImage, ReadAll(imageStream));
        Assert.Equal(NewScript2, ReadAll(script2Stream));
    }

    [Fact]
    public void WriteTo_ComLacunaETrailerAsOutrasEntradasEOsBytesSobrantesSaemIdenticos()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var firstByOffset = ypf.Entries.Where(e => e.SizeStored > 0).OrderBy(e => e.Offset).First();
        var second = ypf.Entries.Where(e => e.SizeStored > 0).OrderBy(e => e.Offset).Skip(1).First();
        byte[] trailer = [1, 2, 3, 4, 5];
        var bytes = WithGap(ypf, (long)second.Offset, 3).Concat(trailer).ToArray();
        using var archive = Open(bytes);

        var output = Write(archive, Replace(firstByOffset.Name, firstByOffset.Name == SyntheticYpfBuilderTests.Script1 ? NewScript1 : NewImage));
        using var rewritten = Open(output);

        var gap = Assert.Single(rewritten.Parameters.Gaps);
        Assert.Equal(3, gap.Length);
        Assert.All(output[(int)gap.Offset..(int)(gap.Offset + gap.Length)], b => Assert.Equal(0xAA, b));
        Assert.Equal(trailer, output[^trailer.Length..]);
        Assert.Equal(trailer.Length, rewritten.Parameters.Trailer.Length);
        foreach (var entry in rewritten.Entries)
        {
            using var stream = rewritten.OpenEntry(entry);
            Assert.NotNull(stream);
        }
    }

    // ---- Recusas ----

    [Fact]
    public void WriteTo_ChaveInexistenteLancaArgumentExceptionSemGravarNada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var output = new MemoryStream();

        var ex = Assert.Throws<ArgumentException>(() => archive.WriteTo(output, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfBuilderTests.Image)] = NewImage,
            ["nao/existe.ybn"] = [1],
        }));

        Assert.Contains("nao/existe.ybn", ex.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_ConteudoNuloLancaArgumentExceptionSemGravarNada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => archive.WriteTo(output, new Dictionary<string, byte[]> { [Key(SyntheticYpfBuilderTests.Image)] = null! }));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_DestinoSemGravacaoOuSemBuscaLancaArgumentException()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var readOnly = new MemoryStream(new byte[8], writable: false);

        Assert.Throws<ArgumentException>(() => archive.WriteTo(readOnly, new Dictionary<string, byte[]>()));
        Assert.Throws<ArgumentException>(() => archive.WriteTo(new NonSeekableStream(), new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void WriteTo_DestinoIgualAoFluxoDeOrigemLancaArgumentException()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var source = new MemoryStream(ypf.Bytes);
        using var archive = (YpfArchive)Format.Open(source);

        var ex = Assert.Throws<ArgumentException>(() => archive.WriteTo(source, new Dictionary<string, byte[]>()));

        Assert.Contains("origem", ex.Message);
        Assert.Equal(ypf.Bytes, source.ToArray());
    }

    [Fact]
    public void WriteTo_DestinoNoMesmoCaminhoDoArquivoDeOrigemLancaArgumentException()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-ypf-{Guid.NewGuid():N}.ypf");
        File.WriteAllBytes(path, ypf.Bytes);
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = (YpfArchive)Format.Open(source);
            using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            Assert.Throws<ArgumentException>(() => archive.WriteTo(other, new Dictionary<string, byte[]>()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteTo_OrigemQueMudouDeTamanhoDepoisDeAbertaLancaInvalidDataSemGravarNada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var source = new MemoryStream(ypf.Bytes.ToArray());
        using var archive = (YpfArchive)Format.Open(source);
        source.SetLength(source.Length - 3);
        using var output = new MemoryStream();

        var ex = Assert.Throws<InvalidDataException>(() => archive.WriteTo(output, Replace(SyntheticYpfBuilderTests.Image, NewImage)));

        Assert.Contains("mudou depois de aberto", ex.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_ArgumentosNulosEPacoteDescartado()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var archive = Open(ypf.Bytes);

        Assert.Throws<ArgumentNullException>(() => archive.WriteTo(null!, new Dictionary<string, byte[]>()));
        Assert.Throws<ArgumentNullException>(() => archive.WriteTo(new MemoryStream(), null!));
        archive.Dispose();
        Assert.Throws<ObjectDisposedException>(() => archive.WriteTo(new MemoryStream(), new Dictionary<string, byte[]>()));
    }

    private static byte[] WithEveryDataHashWrong(SyntheticYpf ypf)
    {
        var bytes = (byte[])ypf.Bytes.Clone();
        foreach (var entry in ypf.Entries)
        {
            bytes[entry.IndexPosition + entry.IndexEntrySize - 4] ^= 0x01;
        }

        return bytes;
    }

    [Fact]
    public void WriteTo_SemAlgoritmoDeHashConfirmadoRecusaTrocaSemGravarNada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(WithEveryDataHashWrong(ypf));
        Assert.False(archive.Parameters.DataHashVerified);
        using var output = new MemoryStream();

        var ex = Assert.Throws<NotSupportedException>(() => archive.WriteTo(output, Replace(SyntheticYpfBuilderTests.Script1, NewScript1)));

        Assert.Contains("hash dos dados", ex.Message);
        Assert.Contains(Key(SyntheticYpfBuilderTests.Script1), ex.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_SemAlgoritmoDeHashConfirmadoAindaCopiaOPacoteSemTrocas()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = WithEveryDataHashWrong(ypf);
        using var archive = Open(bytes);

        Assert.Equal(bytes, Write(archive));
        Assert.Equal(bytes, Write(archive, Replace(SyntheticYpfBuilderTests.Script1, ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script1).Content)));
    }

    [Fact]
    public void WriteTo_DadosSobrepostosSemTrocaCopiamOArquivoETrocaRecusa()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var script1 = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script1);
        var sound = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Sound);
        var bytes = WithOffset(ypf, ypf.Bytes, sound, script1.Offset + 1);
        using var archive = Open(bytes);
        using var output = new MemoryStream();

        Assert.Equal(bytes, Write(archive));
        var ex = Assert.Throws<NotSupportedException>(() => archive.WriteTo(output, Replace(SyntheticYpfBuilderTests.Image, NewImage)));

        Assert.Contains("sobrep", ex.Message);
        Assert.Equal(0, output.Length);
    }

    // ---- Destino reaproveitado, caminho com prefixo e dados compartilhados ----

    [Fact]
    public void WriteTo_DestinoMaisLongoQueOPacoteFicaSemACaudaVelha()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        foreach (var replacements in new[] { new Dictionary<string, byte[]>(), Replace(SyntheticYpfBuilderTests.Image, NewImage) })
        {
            var stale = new byte[100_000];
            Array.Fill(stale, (byte)0xEE);
            using var output = new MemoryStream();
            output.Write(stale);
            output.Position = 0;

            archive.WriteTo(output, replacements);

            Assert.Equal(output.Position, output.Length);
            Assert.True(output.Length < stale.Length);
            using var rewritten = Open(output.ToArray());
            Assert.Equal(0, rewritten.Parameters.Trailer.Length);
            Assert.Empty(rewritten.Parameters.Gaps);
        }
    }

    [Fact]
    public void WriteTo_ArquivoDeDestinoExistenteMaisLongoEhTruncado()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-ypf-{Guid.NewGuid():N}.ypf");
        File.WriteAllBytes(path, new byte[50_000]);
        try
        {
            using (var output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                archive.WriteTo(output, new Dictionary<string, byte[]>());
            }

            Assert.Equal(ypf.Bytes, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteTo_DestinoNoMesmoCaminhoComPrefixoDoWindowsLancaArgumentException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O prefixo \\\\?\\ só existe no Windows.");
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var path = Path.Combine(Path.GetTempPath(), $"vnforge-ypf-{Guid.NewGuid():N}.ypf");
        File.WriteAllBytes(path, ypf.Bytes);
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = (YpfArchive)Format.Open(source);
            using var other = new FileStream(@"\\?\" + path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            Assert.StartsWith(@"\\?\", other.Name, StringComparison.Ordinal);

            Assert.Throws<ArgumentException>(() => archive.WriteTo(other, new Dictionary<string, byte[]>()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SyntheticYpfBuilder SharedSample(SyntheticYpfDataOrder order, byte[]? script2) =>
        new SyntheticYpfBuilder(300)
            .WithDataOrder(order)
            .WithSharedDuplicateData()
            .AddFile(SyntheticYpfBuilderTests.Script1, SyntheticYpfBuilderTests.SmallBinary)
            .AddFile(SyntheticYpfBuilderTests.Image, SyntheticYpfBuilderTests.Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile(SyntheticYpfBuilderTests.Script2, script2 ?? SyntheticYpfBuilderTests.Repetitive, compress: true)
            .AddFile(SyntheticYpfBuilderTests.Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile(SyntheticYpfBuilderTests.Empty, []);

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index)]
    [InlineData(SyntheticYpfDataOrder.Insertion)]
    public void WriteTo_TrocarOSegundoDeDoisQueDividemOBlocoGeraOMesmoArquivoQueOGerador(SyntheticYpfDataOrder order)
    {
        // Imagem e Script2 são vizinhos na inserção e dividem o bloco: trocar o segundo deixa o bloco antigo e põe o novo logo depois.
        var original = SharedSample(order, null).Build();
        var image = original.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Image);
        var script2 = original.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Script2);
        Assert.Equal(image.Offset, script2.Offset);
        using var archive = Open(original.Bytes);

        var output = Write(archive, Replace(SyntheticYpfBuilderTests.Script2, NewScript2));

        using var rewritten = Open(output);
        using var imageStream = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Image)));
        using var script2Stream = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfBuilderTests.Script2)));
        Assert.Equal(SyntheticYpfBuilderTests.Repetitive, ReadAll(imageStream));
        Assert.Equal(NewScript2, ReadAll(script2Stream));
        if (order == SyntheticYpfDataOrder.Insertion)
        {
            Assert.Equal(SharedSample(order, NewScript2).Build().Bytes, output);
        }
    }

    [Theory]
    [InlineData(SyntheticYpfDataOrder.Index)]
    [InlineData(SyntheticYpfDataOrder.Insertion)]
    public void WriteTo_DadosCompartilhadosComLacunaETrailerPreservamTudoAoTrocarUmaEntrada(SyntheticYpfDataOrder order)
    {
        var ypf = SharedSample(order, null).Build();
        var shared = ypf.Entries.Single(e => e.Name == SyntheticYpfBuilderTests.Image);
        byte[] trailer = [1, 2, 3, 4, 5];
        var bytes = WithGap(ypf, (long)shared.Offset, 3).Concat(trailer).ToArray();
        using var archive = Open(bytes);
        Assert.Equal(bytes, Write(archive));

        var output = Write(archive, Replace(SyntheticYpfBuilderTests.Image, NewImage));

        using var rewritten = Open(output);
        var gap = Assert.Single(rewritten.Parameters.Gaps);
        Assert.All(output[(int)gap.Offset..(int)(gap.Offset + gap.Length)], b => Assert.Equal(0xAA, b));
        Assert.Equal(trailer, output[^trailer.Length..]);
        foreach (var entry in rewritten.Entries)
        {
            var expected = entry.Path == Key(SyntheticYpfBuilderTests.Image)
                ? NewImage
                : ypf.Entries.Single(e => Key(e.Name) == entry.Path).Content;
            using var stream = rewritten.OpenEntry(entry);
            Assert.Equal(expected, ReadAll(stream));
        }
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
