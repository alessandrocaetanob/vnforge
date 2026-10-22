using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Os 32 bytes fixos que abrem um YPF: <c>"YPF\0"</c>, versão, contagem de arquivos, <c>header_size</c> e 16 bytes
/// de padding. O índice começa logo depois.
/// </summary>
/// <param name="Version">Versão do formato.</param>
/// <param name="FileCount">Quantidade de entradas do índice.</param>
/// <param name="HeaderSize">
/// O campo <c>header_size</c> como gravado. Pode incluir os 32 bytes do cabeçalho ou não, conforme a versão e
/// o programa que gravou; <see cref="YpfParameters.HeaderSizeIncludesHeader"/> diz qual semântica o arquivo usa.
/// </param>
public readonly record struct YpfHeader(uint Version, uint FileCount, uint HeaderSize)
{
    /// <summary>Tamanho do cabeçalho fixo; o índice começa neste offset.</summary>
    public const int Size = 0x20;

    private static ReadOnlySpan<byte> Magic => "YPF\0"u8;

    /// <summary>Confere se os bytes começam com a assinatura <c>YPF\0</c>.</summary>
    public static bool HasMagic(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);

    /// <summary>Lê o cabeçalho; devolve falso se faltam bytes ou a assinatura não confere.</summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out YpfHeader header)
    {
        if (bytes.Length < Size || !HasMagic(bytes))
        {
            header = default;
            return false;
        }

        header = new YpfHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
        return true;
    }
}
