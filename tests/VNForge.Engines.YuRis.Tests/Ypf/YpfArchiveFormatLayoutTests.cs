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

    // ---- Índice grande, header_size e limite de leitura ----

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
