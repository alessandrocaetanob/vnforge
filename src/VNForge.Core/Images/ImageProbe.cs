using System.Buffers.Binary;

namespace VNForge.Core.Images;

/// <summary>Formato de imagem reconhecido pela assinatura dos primeiros bytes.</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    WebP,
    Bmp,
}

/// <summary>Formato e tamanho de uma imagem, lidos do cabeçalho.</summary>
public sealed record ImageInfo(ImageFormat Format, int Width, int Height)
{
    /// <summary>
    /// O <c>media_type</c> que a API do Claude aceita num bloco de imagem (PNG, JPEG, GIF e WebP), ou nulo para o que ela não lê (BMP).
    /// </summary>
    public string? MediaType => Format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Gif => "image/gif",
        ImageFormat.WebP => "image/webp",
        _ => null,
    };

    public long Pixels => (long)Width * Height;
}

/// <summary>
/// Lê só o cabeçalho de uma imagem: formato e tamanho, sem decodificar os pixels. Arquivo cortado, de formato desconhecido ou com tamanho zero
/// devolve nulo, o que o inventário trata como "não decodificável".
/// </summary>
public static class ImageProbe
{
    public static ImageInfo? Read(ReadOnlySpan<byte> data)
    {
        var info = data switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ReadPng(data),
            [0xFF, 0xD8, 0xFF, ..] => ReadJpeg(data),
            [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ReadGif(data),
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ReadWebP(data),
            [(byte)'B', (byte)'M', ..] => ReadBmp(data),
            _ => null,
        };

        return info is { Width: > 0, Height: > 0 } ? info : null;
    }

    private static ImageInfo? ReadPng(ReadOnlySpan<byte> data)
    {
        // Assinatura (8), tamanho do chunk (4), "IHDR" (4), largura e altura (4 + 4).
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        return new ImageInfo(ImageFormat.Png, BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
    }

    private static ImageInfo? ReadGif(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10 || !(data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        return new ImageInfo(ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
    }

    private static ImageInfo? ReadBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26)
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        if (headerSize == 12)
        {
            return new ImageInfo(ImageFormat.Bmp, BinaryPrimitives.ReadUInt16LittleEndian(data[18..]), BinaryPrimitives.ReadUInt16LittleEndian(data[20..]));
        }

        if (headerSize < 40)
        {
            return null;
        }

        // Altura negativa quer dizer "linhas de cima para baixo"; o tamanho é o módulo.
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
        return new ImageInfo(ImageFormat.Bmp, BinaryPrimitives.ReadInt32LittleEndian(data[18..]), height == int.MinValue ? 0 : Math.Abs(height));
    }

    private static ImageInfo? ReadJpeg(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
            {
                return null;
            }

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            // SOI/EOI/RSTn/TEM não têm tamanho; chegar ao EOI ou aos dados sem achar um SOF é arquivo sem tamanho.
            if (HasNoLength(marker))
            {
                position += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            if (IsStartOfFrame(marker))
            {
                return ReadStartOfFrame(data, position);
            }

            if (length < 2)
            {
                return null;
            }

            position += 2 + length;
        }

        return null;
    }

    private static bool HasNoLength(byte marker) => marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7);

    private static bool IsStartOfFrame(byte marker) => marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    // Segmento SOF: comprimento (2), precisão (1), altura (2), largura (2).
    private static ImageInfo? ReadStartOfFrame(ReadOnlySpan<byte> data, int position)
    {
        if (position + 9 > data.Length)
        {
            return null;
        }

        return new ImageInfo(
            ImageFormat.Jpeg,
            BinaryPrimitives.ReadUInt16BigEndian(data[(position + 7)..]),
            BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]));
    }

    private static ImageInfo? ReadWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
        {
            return null;
        }

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            return new ImageInfo(ImageFormat.WebP, ReadUInt24(data[24..]) + 1, ReadUInt24(data[27..]) + 1);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (data[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return new ImageInfo(ImageFormat.WebP, (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Etiqueta do quadro (3 bytes), código de início 9D 01 2A, largura e altura em 14 bits.
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
            {
                return null;
            }

            return new ImageInfo(
                ImageFormat.WebP,
                BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        }

        return null;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);
}
