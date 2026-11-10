using System.Buffers.Binary;
using System.IO.Compression;

namespace VNForge.Core.Library;

/// <summary>Grava uma imagem como PNG RGB de 8 bits, sem alfa e sem biblioteca de imagem: a captura só precisa disso.</summary>
internal static class PngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(WindowImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8; // bits por canal
        header[9] = 2; // cor verdadeira, sem alfa
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, Compress(image));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    // Filtro Sub (1) em toda linha: o fundo liso e as faixas de uma cena de VN viram zeros, que o deflate come.
    private static byte[] Compress(WindowImage image)
    {
        var stride = image.Width * 3;
        var row = new byte[stride + 1];
        row[0] = 1;
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < image.Height; y++)
            {
                var source = image.Bgra.AsSpan(y * image.Width * 4, image.Width * 4);
                for (var x = 0; x < image.Width; x++)
                {
                    var at = x * 4;
                    var to = 1 + (x * 3);
                    row[to] = (byte)(source[at + 2] - (x > 0 ? source[at - 2] : 0));
                    row[to + 1] = (byte)(source[at + 1] - (x > 0 ? source[at - 3] : 0));
                    row[to + 2] = (byte)(source[at] - (x > 0 ? source[at - 4] : 0));
                }

                zlib.Write(row);
            }
        }

        return packed.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
