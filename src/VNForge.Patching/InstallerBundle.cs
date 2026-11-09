using System.Buffers.Binary;

namespace VNForge.Patching;

/// <summary>
/// O instalador de um arquivo só: o executável do instalador, o <c>.vnfpatch</c> logo depois e, no fim, o tamanho do patch (int64) e a marca
/// <c>VNFINST1</c>. O executável abre a si mesmo e procura a marca.
/// </summary>
public static class InstallerBundle
{
    private static ReadOnlySpan<byte> Tail => "VNFINST1"u8;

    private const int TailSize = 16;

    /// <summary>Junta o executável do instalador e o pacote em <paramref name="outputPath"/>.</summary>
    public static void Create(string installerExecutable, Stream package, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerExecutable);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        package.Position = 0;
        var length = package.Length;
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var stub = new FileStream(installerExecutable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stub.CopyTo(output);
        }

        package.CopyTo(output);
        Span<byte> tail = stackalloc byte[TailSize];
        BinaryPrimitives.WriteInt64LittleEndian(tail, length);
        Tail.CopyTo(tail[8..]);
        output.Write(tail);
    }

    /// <summary>O patch embutido em <paramref name="executablePath"/>; nulo quando o executável não tem patch (um instalador vazio).</summary>
    public static PatchPackage? TryOpen(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var file = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (file.Length < TailSize)
            {
                file.Dispose();
                return null;
            }

            Span<byte> tail = stackalloc byte[TailSize];
            file.Seek(-TailSize, SeekOrigin.End);
            file.ReadExactly(tail);
            var length = BinaryPrimitives.ReadInt64LittleEndian(tail);
            if (!tail[8..].SequenceEqual(Tail) || length <= 0 || length > file.Length - TailSize)
            {
                file.Dispose();
                return null;
            }

            return PatchPackage.Open(new SliceStream(file, file.Length - TailSize - length, length));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private sealed class SliceStream(Stream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var wanted = (int)Math.Min(buffer.Length, Math.Max(0, length - _position));
            if (wanted == 0)
            {
                return 0;
            }

            inner.Position = start + _position;
            var read = inner.Read(buffer[..wanted]);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => length + offset,
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
