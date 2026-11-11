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

    /// <summary>A amostra de <see cref="SyntheticYpfSamples.SampleBuilder"/> com o conteúdo de um arquivo trocado: o oráculo do escritor.</summary>
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
            .AddFile(SyntheticYpfSamples.Script1, Pick(SyntheticYpfSamples.Script1, SyntheticYpfSamples.SmallBinary), SyntheticYpfFileType.Ybn)
            .AddFile(SyntheticYpfSamples.Image, Pick(SyntheticYpfSamples.Image, SyntheticYpfSamples.Repetitive), SyntheticYpfFileType.Png, compress: true)
            .AddFile(SyntheticYpfSamples.Sound, Pick(SyntheticYpfSamples.Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9]), SyntheticYpfFileType.Ogg)
            .AddFile(SyntheticYpfSamples.Script2, Pick(SyntheticYpfSamples.Script2, SyntheticYpfSamples.Repetitive), SyntheticYpfFileType.Ybn, compress: true)
            .AddFile(SyntheticYpfSamples.Empty, [], SyntheticYpfFileType.Ybn);
    }

    private static readonly (string Name, byte[] Content)[] OracleCases =
    [
        (SyntheticYpfSamples.Image, NewImage),
        (SyntheticYpfSamples.Script2, NewScript2),
        (SyntheticYpfSamples.Script1, NewScript1),
        (SyntheticYpfSamples.Sound, NewSound),
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
    [InlineData(SyntheticYpfDataOrder.Insertion, true)]
    public void WriteTo_SemTrocasGeraArquivoIdenticoComOrdemDeDadosEDuplicatas(SyntheticYpfDataOrder order, bool shared)
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).WithDataOrder(order).WithSharedDuplicateData(shared).Build();
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
        var ypf = SyntheticYpfSamples.DefaultSample(300).WithDataHash(dataHash).Build();
        using var archive = Open(ypf.Bytes);

        Assert.Equal(ypf.Bytes, Write(archive));
    }

    [Fact]
    public void WriteTo_PreservaPaddingLacunasETrailerNaIdaEVolta()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
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
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var bytes = WithEntryNameXor(ypf, ypf.Bytes, SyntheticYpfSamples.Script2, 0x55);
        using var archive = Open(bytes);

        Assert.Equal(bytes, Write(archive));
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
    [MemberData(nameof(CoreVariants))]
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
    [MemberData(nameof(CoreVariants))]
    public void WriteTo_ComUmaEntradaTrocadaAsOutrasSaemIdenticasETrocadaELidaDeVolta(
        uint version,
        SyntheticYpfOffsetSize offsetSize,
        SyntheticYpfHashAlgorithm nameHash,
        SyntheticYpfHashAlgorithm dataHash)
    {
        var original = Sample(version, offsetSize, nameHash, dataHash).Build();
        using var source = Open(original.Bytes);
        var output = Write(source, Replace(SyntheticYpfSamples.Image, NewImage));

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
            if (before.Path == Key(SyntheticYpfSamples.Image))
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
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        // Os 20 bytes de NewScript1 e o conteúdo de 6000 bytes do Image: a entrada não comprimida continua crua e a comprimida continua zlib.
        using var rewritten = Open(Write(archive, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfSamples.Script1)] = NewScript1,
            [Key(SyntheticYpfSamples.Image)] = [1, 2, 3],
        }));

        var script1 = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Script1));
        var image = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Image));
        Assert.False(script1.IsCompressed);
        Assert.Equal((uint)NewScript1.Length, script1.SizeStored);
        Assert.True(image.IsCompressed);
        using var stream = rewritten.OpenEntry(image);
        Assert.Equal(new byte[] { 1, 2, 3 }, ReadAll(stream));
    }

    [Theory]
    [InlineData(SyntheticYpfHashAlgorithm.Adler32)]
    [InlineData(SyntheticYpfHashAlgorithm.MurmurHash2)]
    public void WriteTo_RecalculaOHashDosDadosComOAlgoritmoDetectado(SyntheticYpfHashAlgorithm dataHash)
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).WithDataHash(dataHash).Build();
        using var archive = Open(ypf.Bytes);
        var output = Write(archive, Replace(SyntheticYpfSamples.Script1, NewScript1));

        using var rewritten = Open(output);
        var entry = rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Script1));

        Assert.Equal(Map(dataHash), rewritten.Parameters.DataHash);
        Assert.True(rewritten.Parameters.DataHashVerified);
        Assert.Equal(YpfHash.Compute(Map(dataHash), NewScript1), entry.DataHash);
    }

    [Fact]
    public void WriteTo_TrocaPorConteudoVazioELidaDeVolta()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);

        using var rewritten = Open(Write(archive, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfSamples.Image)] = [],
            [Key(SyntheticYpfSamples.Script1)] = [],
        }));

        foreach (var name in new[] { SyntheticYpfSamples.Image, SyntheticYpfSamples.Script1 })
        {
            using var stream = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(name)));
            Assert.Empty(ReadAll(stream));
        }

        using var sound = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Sound)));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, ReadAll(sound));
    }

    [Fact]
    public void WriteTo_TrocaDeVariasEntradasDeUmaVez()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(500).Build();
        using var archive = Open(ypf.Bytes);
        var replacements = new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfSamples.Script1)] = NewScript1,
            [Key(SyntheticYpfSamples.Image)] = NewImage,
            [Key(SyntheticYpfSamples.Script2)] = NewScript2,
            [Key(SyntheticYpfSamples.Empty)] = NewSound,
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
        var ypf = SyntheticYpfSamples.DefaultSample(300).WithSharedDuplicateData().Build();
        var image = ypf.Entries.Single(e => e.Name == SyntheticYpfSamples.Image);
        var script2 = ypf.Entries.Single(e => e.Name == SyntheticYpfSamples.Script2);
        Assert.Equal(image.Offset, script2.Offset);
        using var archive = Open(ypf.Bytes);

        var output = Write(archive, Replace(SyntheticYpfSamples.Image, NewImage));
        using var rewritten = Open(output);

        using var newImage = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Image)));
        using var oldScript2 = rewritten.OpenEntry(rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Script2)));
        Assert.Equal(NewImage, ReadAll(newImage));
        Assert.Equal(SyntheticYpfSamples.Repetitive, ReadAll(oldScript2));
        Assert.Equal(script2.Offset, rewritten.Entries.Single(e => e.Path == Key(SyntheticYpfSamples.Script2)).Offset);
    }

    // ---- Recusas ----

    [Fact]
    public void WriteTo_ChaveInexistenteLancaArgumentExceptionSemGravarNada()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var output = new MemoryStream();

        var ex = Assert.Throws<ArgumentException>(() => archive.WriteTo(output, new Dictionary<string, byte[]>
        {
            [Key(SyntheticYpfSamples.Image)] = NewImage,
            ["nao/existe.ybn"] = [1],
        }));

        Assert.Contains("nao/existe.ybn", ex.Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_ConteudoNuloLancaArgumentExceptionSemGravarNada()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => archive.WriteTo(output, new Dictionary<string, byte[]> { [Key(SyntheticYpfSamples.Image)] = null! }));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void WriteTo_DestinoSemGravacaoOuSemBuscaLancaArgumentException()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        using var archive = Open(ypf.Bytes);
        using var readOnly = new MemoryStream(new byte[8], writable: false);

        Assert.Throws<ArgumentException>(() => archive.WriteTo(readOnly, new Dictionary<string, byte[]>()));
        Assert.Throws<ArgumentException>(() => archive.WriteTo(new NonSeekableStream(), new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void WriteTo_DestinoIgualAoFluxoDeOrigemLancaArgumentException()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var source = new MemoryStream(ypf.Bytes);
        using var archive = (YpfArchive)Format.Open(source);

        var ex = Assert.Throws<ArgumentException>(() => archive.WriteTo(source, new Dictionary<string, byte[]>()));

        Assert.Contains("origem", ex.Message);
        Assert.Equal(ypf.Bytes, source.ToArray());
    }

    [Fact]
    public void WriteTo_OrigemQueMudouDeTamanhoDepoisDeAbertaLancaInvalidDataSemGravarNada()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var source = new MemoryStream(ypf.Bytes.ToArray());
        using var archive = (YpfArchive)Format.Open(source);
        source.SetLength(source.Length - 3);
        using var output = new MemoryStream();

        var ex = Assert.Throws<InvalidDataException>(() => archive.WriteTo(output, Replace(SyntheticYpfSamples.Image, NewImage)));

        Assert.Contains("mudou depois de aberto", ex.Message);
        Assert.Equal(0, output.Length);
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
    public void WriteTo_DadosSobrepostosSemTrocaCopiamOArquivoETrocaRecusa()
    {
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
        var script1 = ypf.Entries.Single(e => e.Name == SyntheticYpfSamples.Script1);
        var sound = ypf.Entries.Single(e => e.Name == SyntheticYpfSamples.Sound);
        var bytes = WithOffset(ypf, ypf.Bytes, sound, script1.Offset + 1);
        using var archive = Open(bytes);
        using var output = new MemoryStream();

        Assert.Equal(bytes, Write(archive));
        var ex = Assert.Throws<NotSupportedException>(() => archive.WriteTo(output, Replace(SyntheticYpfSamples.Image, NewImage)));

        Assert.Contains("sobrep", ex.Message);
        Assert.Equal(0, output.Length);
    }

    // ---- Destino reaproveitado, caminho com prefixo e dados compartilhados ----

    [Fact]
    public void WriteTo_DestinoNoMesmoCaminhoComPrefixoDoWindowsLancaArgumentException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O prefixo \\\\?\\ só existe no Windows.");
        var ypf = SyntheticYpfSamples.DefaultSample(300).Build();
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
            .AddFile(SyntheticYpfSamples.Script1, SyntheticYpfSamples.SmallBinary)
            .AddFile(SyntheticYpfSamples.Image, SyntheticYpfSamples.Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile(SyntheticYpfSamples.Script2, script2 ?? SyntheticYpfSamples.Repetitive, compress: true)
            .AddFile(SyntheticYpfSamples.Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile(SyntheticYpfSamples.Empty, []);

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
