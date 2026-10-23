using System.Buffers.Binary;
using VNForge.Core;
using VNForge.Engines.YuRis.Tests.Ypf.Synthetic;
using VNForge.Engines.YuRis.Ypf;

namespace VNForge.Engines.YuRis.Tests.Ypf;

public partial class YpfArchiveFormatTests
{
    // ---- Mutações de layout: o que a gravação (#29) precisa copiar do original ----

    private static int OffsetFieldPosition(SyntheticYpf ypf, SyntheticYpfEntry entry) =>
        entry.IndexPosition + entry.IndexEntrySize - 4 - (ypf.OffsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4);

    private static byte[] WithOffset(SyntheticYpf ypf, byte[] bytes, SyntheticYpfEntry entry, ulong offset)
    {
        var copy = (byte[])bytes.Clone();
        var position = OffsetFieldPosition(ypf, entry);
        if (ypf.OffsetSize == SyntheticYpfOffsetSize.Bits64)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(position), offset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(position), (uint)offset);
        }

        return copy;
    }

    /// <summary>Insere <paramref name="count"/> bytes 0xAA no offset <paramref name="at"/> da área de dados e corrige os offsets que passam dele.</summary>
    private static byte[] WithGap(SyntheticYpf ypf, long at, int count)
    {
        var bytes = ypf.Bytes[..(int)at].Concat(Enumerable.Repeat((byte)0xAA, count)).Concat(ypf.Bytes[(int)at..]).ToArray();
        foreach (var entry in ypf.Entries.Where(e => (long)e.Offset >= at))
        {
            bytes = WithOffset(ypf, bytes, entry, entry.Offset + (ulong)count);
        }

        return bytes;
    }

    /// <summary>Regrava o nome da entrada cifrado com outro XOR efetivo (o hash do nome, sobre o nome em claro, não muda).</summary>
    private static byte[] WithEntryNameXor(SyntheticYpf ypf, byte[] bytes, string entryName, byte newXor)
    {
        var entry = ypf.Entries.Single(e => e.Name == entryName);
        var copy = (byte[])bytes.Clone();
        for (var i = 0; i < entry.NameBytes.Length; i++)
        {
            copy[entry.IndexPosition + 5 + i] = (byte)(entry.NameBytes[i] ^ newXor);
        }

        return copy;
    }

    [Fact]
    public void Open_PaddingNaoNuloDoCabecalhoFicaRegistrado()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var padding = Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i)).ToArray();
        var bytes = (byte[])ypf.Bytes.Clone();
        padding.CopyTo(bytes, 16);

        using var archive = Open(bytes);

        Assert.Equal(padding, archive.Header.Padding.ToArray());
        Assert.False(archive.Header.HasZeroPadding);
        Assert.NotEqual(new YpfHeader(ypf.Version, ypf.FileCount, ypf.HeaderSize, new byte[16]), archive.Header);
        Assert.Equal(ypf.Entries.Count, archive.Entries.Count);
    }

    [Fact]
    public void Open_BytesDepoisDoUltimoDadoViramTrailer()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(500).Build();
        var bytes = ypf.Bytes.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray();

        using var archive = Open(bytes);

        Assert.Empty(archive.Parameters.Gaps);
        Assert.Equal(new YpfByteRange(ypf.Bytes.Length, 5), archive.Parameters.Trailer);
        Assert.Equal(bytes.Length, archive.Parameters.FileLength);
    }

    [Fact]
    public void Open_LacunaAntesDaPrimeiraEntradaFicaRegistrada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();

        using var archive = Open(WithGap(ypf, ypf.DataStart, 4));

        Assert.Equal([new YpfByteRange(ypf.DataStart, 4)], archive.Parameters.Gaps);
        Assert.Equal(0, archive.Parameters.Trailer.Length);
        Assert.True(archive.Parameters.DataHashVerified);
    }

    [Fact]
    public void Open_LacunaEntreEntradasFicaRegistrada()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var second = ypf.Entries.Where(e => e.SizeStored > 0).OrderBy(e => e.Offset).Skip(1).First();

        using var archive = Open(WithGap(ypf, (long)second.Offset, 3));

        Assert.Equal([new YpfByteRange((long)second.Offset, 3)], archive.Parameters.Gaps);
        Assert.Equal(0, archive.Parameters.Trailer.Length);
    }

    [Fact]
    public void Open_EntradaSemBytesComOffsetDentroDoIndiceEhAceita()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var empty = ypf.Entries.Single(e => e.SizeStored == 0);

        using var archive = Open(WithOffset(ypf, ypf.Bytes, empty, 0));

        Assert.Equal(0ul, archive.Entries.Single(e => e.Path == empty.Name.Replace('\\', '/')).Offset);
    }

    [Fact]
    public void Open_EntradaComBytesComOffsetDentroDoIndiceLancaErro()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var entry = ypf.Entries.First(e => e.SizeStored > 0);

        AssertInvalid(WithOffset(ypf, ypf.Bytes, entry, 0), "dentro do cabeçalho ou do índice", "offset 0");
    }

    // ---- Nomes duplicados e chave por entrada ----

    [Fact]
    public void Open_NomesQueSoDiferemNoSeparadorSaoDuplicadosPelosBytes()
    {
        var ypf = new SyntheticYpfBuilder(300)
            .AddFile(@"x\表.ybn", [1])
            .AddFile("x/表.ybn", [2])
            .Build();

        AssertInvalid(ypf.Bytes, "mais de uma entrada");
    }

    [Fact]
    public void Open_SegundoByteShiftJisIgualABarraInvertidaNaoViraSeparador()
    {
        var ypf = new SyntheticYpfBuilder(300)
            .AddFile("表.ybn", [1])
            .AddFile("x/表.ybn", [2])
            .Build();

        using var archive = Open(ypf.Bytes);

        Assert.Equal(["x/表.ybn", "表.ybn"], archive.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Open_CadaEntradaPodeTerSuaPropriaChaveDoNome()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();
        var bytes = WithEntryNameXor(ypf, ypf.Bytes, ypf.Entries[1].Name, 0x3C);
        bytes = WithEntryNameXor(ypf, bytes, ypf.Entries[3].Name, 0xA7);

        using var archive = Open(bytes);

        Assert.Equal(
            ypf.Entries.Select(e => e.Name.Replace('\\', '/')),
            archive.Entries.Select(e => e.Path));
        Assert.Equal(
            [ypf.EffectiveNameXor, (byte)0x3C, ypf.EffectiveNameXor, (byte)0xA7, ypf.EffectiveNameXor],
            archive.Entries.Select(e => e.NameXor));
        Assert.Equal(ypf.EffectiveNameXor, archive.Parameters.NameXor);
    }

    [Fact]
    public void Open_NomesSemPontoComChavesDiferentesUsamAChaveAnteriorEDepoisAVarredura()
    {
        var builder = new SyntheticYpfBuilder(300).WithNameKey(0x2B);
        foreach (var name in new[] { "alfa", "bravo", "charlie", "delta", "echo", "foxtrot" })
        {
            builder.AddFile(name, [1]);
        }

        var ypf = builder.Build();
        var bytes = WithEntryNameXor(ypf, ypf.Bytes, ypf.Entries[2].Name, 0x91);
        bytes = WithEntryNameXor(ypf, bytes, ypf.Entries[4].Name, 0x91);

        using var archive = Open(bytes);

        Assert.Equal(ypf.Entries.Select(e => e.Name), archive.Entries.Select(e => e.Path));
        Assert.Equal(
            [ypf.EffectiveNameXor, ypf.EffectiveNameXor, (byte)0x91, ypf.EffectiveNameXor, (byte)0x91, ypf.EffectiveNameXor],
            archive.Entries.Select(e => e.NameXor));
    }

    // ---- Índice grande, header_size e limite de leitura ----

    [Theory]
    [InlineData(300u, SyntheticYpfDataOrder.Index)]
    [InlineData(500u, SyntheticYpfDataOrder.Insertion)]
    public void Open_LeUmIndiceComAlgunsMilharesDeEntradas(uint version, SyntheticYpfDataOrder order)
    {
        var builder = new SyntheticYpfBuilder(version).WithDataOrder(order);
        for (var i = 0; i < 3000; i++)
        {
            builder.AddFile($@"dir{i % 7}\arquivo_{i:D5}.ybn", [(byte)i, (byte)(i >> 8)]);
        }

        var ypf = builder.Build();

        using var archive = Open(ypf.Bytes);

        AssertMatches(ypf, archive);
    }

    [Fact]
    public void Open_HeaderSizeGiganteCaiNaLeituraPelaContagemEDizOQueEsperava()
    {
        var ypf = SyntheticYpfBuilderTests.DefaultSample(300).Build();

        AssertInvalid(ypf.WithHeaderSize(0xFFFFFFF0), "header_size gravado é 4294967280");
    }

    [Fact]
    public void Open_IndiceMaiorQueOLimiteDeLeituraCitaOLimite()
    {
        const int fileLength = 200 * 1024 * 1024;
        var head = new byte[YpfHeader.Size];
        "YPF\0"u8.CopyTo(head);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 300);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 5_000_000);
        using var stream = new ZeroTailStream(head, fileLength);

        var ex = Assert.Throws<InvalidFileException>(() => Format.Open(stream));

        Assert.Contains("128 MiB", ex.Message);
    }

    [Fact]
    public void Open_ContagemQuePassaDoLimiteDeIndiceCitaOLimite()
    {
        const long fileLength = 300L * 1024 * 1024;
        var head = new byte[YpfHeader.Size];
        "YPF\0"u8.CopyTo(head);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 300);
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(8), 10_000_000);
        using var stream = new ZeroTailStream(head, fileLength);

        var ex = Assert.Throws<InvalidFileException>(() => Format.Open(stream));

        Assert.Contains("128 MiB", ex.Message);
    }

    /// <summary>Fluxo só de leitura que devolve o cabeçalho dado e zeros até o tamanho informado, sem alocar o arquivo.</summary>
    private sealed class ZeroTailStream(byte[] head, long length) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, Math.Max(0, length - Position));
            for (var i = 0; i < count; i++)
            {
                var p = Position + i;
                buffer[i] = p < head.Length ? head[p] : (byte)0;
            }

            Position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                _ => length + offset,
            };
            return Position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

#pragma warning disable S3218 // Stream.Write fixa o nome; o auxiliar Write da classe externa e inevitavel aqui
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
#pragma warning restore S3218
    }
}
