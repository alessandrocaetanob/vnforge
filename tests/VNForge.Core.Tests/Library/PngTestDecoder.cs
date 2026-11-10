using System.Buffers.Binary;
using System.IO.Compression;

namespace VNForge.Core.Tests.Library;

/// <summary>Lê de volta o PNG que o <c>PngEncoder</c> grava (RGB de 8 bits, filtros 0 e 1) e confere os CRCs: o teste não confia no que o codificador diz de si.</summary>
internal static class PngTestDecoder
{
    public static (int Width, int Height, byte[] Rgb) Decode(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int width = 0, height = 0;
        using var data = new MemoryStream();
        var at = 8;
        var ended = false;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var body = png.AsSpan(at + 8, length);
            var expected = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(expected, Crc32(png.AsSpan(at + 4, 4 + length)));
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    Assert.Equal(new byte[] { 8, 2, 0, 0, 0 }, body[8..].ToArray());
                    break;
                case "IDAT":
                    data.Write(body);
                    break;
                case "IEND":
                    ended = true;
                    break;
            }

            at += 12 + length;
        }

        Assert.True(ended);
        data.Position = 0;
        using var zlib = new ZLibStream(data, CompressionMode.Decompress);
        var stride = width * 3;
        var rgb = new byte[stride * height];
        var row = new byte[stride + 1];
        for (var y = 0; y < height; y++)
        {
            zlib.ReadExactly(row);
            for (var i = 0; i < stride; i++)
            {
                var value = row[i + 1];
                rgb[(y * stride) + i] = row[0] switch
                {
                    0 => value,
                    1 => (byte)(value + (i >= 3 ? rgb[(y * stride) + i - 3] : 0)),
                    _ => throw new InvalidDataException($"Filtro {row[0]} que o codificador não usa."),
                };
            }
        }

        Assert.Equal(-1, zlib.ReadByte());
        return (width, height, rgb);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
