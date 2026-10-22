using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Ypf.Hashing;

/// <summary>
/// Os três hashes de 32 bits que os índices YPF usam, conforme a versão do arquivo (docs/06-yuris-notas-tecnicas.md).
/// </summary>
public static class YpfHash
{
    private const uint AdlerModulus = 65521;
    // Maior n tal que 255*n*(n+1)/2 + (n+1)*(AdlerModulus-1) cabe em 32 bits (zlib, adler32.c).
    private const int AdlerBlockSize = 5552;

    private const uint MurmurM = 0x5bd1e995;
    private const int MurmurR = 24;

    /// <summary>Adler-32 conforme a RFC 1950, seção 8.2.</summary>
    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1;
        uint b = 0;

        while (!data.IsEmpty)
        {
            var block = data[..Math.Min(data.Length, AdlerBlockSize)];
            data = data[block.Length..];

            foreach (var value in block)
            {
                a += value;
                b += a;
            }

            a %= AdlerModulus;
            b %= AdlerModulus;
        }

        return (b << 16) | a;
    }

    /// <summary>CRC-32 (ISO-HDLC, o mesmo do zlib), via <see cref="System.IO.Hashing.Crc32"/>.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data) => System.IO.Hashing.Crc32.HashToUInt32(data);

    /// <summary>
    /// MurmurHash2 de 32 bits de Austin Appleby (leitura little-endian, cauda tratada como no original em C).
    /// O YPF usa seed 0.
    /// </summary>
    public static uint MurmurHash2(ReadOnlySpan<byte> data, uint seed = 0)
    {
        var hash = seed ^ (uint)data.Length;

        while (data.Length >= 4)
        {
            var k = BinaryPrimitives.ReadUInt32LittleEndian(data);
            k *= MurmurM;
            k ^= k >> MurmurR;
            k *= MurmurM;

            hash *= MurmurM;
            hash ^= k;

            data = data[4..];
        }

        switch (data.Length)
        {
            case 3:
                hash ^= (uint)data[2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)data[1] << 8;
                goto case 1;
            case 1:
                hash ^= data[0];
                hash *= MurmurM;
                break;
        }

        hash ^= hash >> 13;
        hash *= MurmurM;
        hash ^= hash >> 15;

        return hash;
    }
}
