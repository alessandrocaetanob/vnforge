using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// XOR cíclico de 4 bytes das seções do YSTB. A chave vai em ordem big-endian (<c>0xD36FAC96</c> cifra com
/// <c>D3 6F AC 96</c>) e recomeça no início de cada seção; a operação é a mesma para cifrar e decifrar.
/// </summary>
internal static class YstbCipher
{
    public static void Apply(ReadOnlySpan<byte> source, uint key, Span<byte> destination)
    {
        Span<byte> keyBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyBytes, key);
        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = (byte)(source[i] ^ keyBytes[i & 3]);
        }
    }
}
