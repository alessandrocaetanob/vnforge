using System.Buffers.Binary;
using System.IO.Compression;

namespace VNForge.Core.Tests.Images;

/// <summary>Imagens geradas em código para os testes: cabeçalhos válidos, pixels lisos. Nenhuma vem de jogo.</summary>
public static class SyntheticImages
{
    /// <summary>PNG em tons de cinza, 8 bits, todo preenchido com <paramref name="shade"/>; <paramref name="shade"/> diferente dá bytes diferentes.</summary>
    public static byte[] Png(int width, int height, byte shade = 0)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[width + 1];
            Array.Fill(row, shade, 1, width);
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>JPEG mínimo: só o que o leitor de tamanho precisa (SOI, um APP0 e o SOF0).</summary>
    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[9]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    public static byte[] Gif(int width, int height)
    {
        var bytes = new List<byte>("GIF89a"u8.ToArray()) { (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0, 0, 0, 0x3B };
        return [.. bytes];
    }

    /// <summary>BMP com cabeçalho BITMAPINFOHEADER (40 bytes) e nenhum pixel; a API do modelo não lê BMP.</summary>
    public static byte[] Bmp(int width, int height)
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        bytes[26] = 1;
        bytes[28] = 24;
        return bytes;
    }

    /// <summary>WebP com chunk VP8X (o que carrega o tamanho sem precisar de pixels).</summary>
    public static byte[] WebPExtended(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WEBPVP8X"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 10);
        WriteUInt24(bytes.AsSpan(24), width - 1);
        WriteUInt24(bytes.AsSpan(27), height - 1);
        return bytes;
    }

    /// <summary>WebP com chunk VP8L (sem perdas).</summary>
    public static byte[] WebPLossless(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WEBPVP8L"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 10);
        bytes[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21), (uint)((width - 1) | ((height - 1) << 14)));
        return bytes;
    }

    /// <summary>WebP com chunk VP8 (com perdas).</summary>
    public static byte[] WebPLossy(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WEBPVP8 "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 10);
        bytes[23] = 0x9D;
        bytes[24] = 0x01;
        bytes[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), (ushort)height);
        return bytes;
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        output.Write(body);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
