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
/// <param name="Padding">
/// Os 16 bytes de padding (offsets 0x10 a 0x1F) como estão no arquivo. Os escritores conhecidos gravam zeros, mas
/// um YPF real pode ter outro valor; a gravação (#29) copia estes bytes para a ida e volta ser idêntica.
/// </param>
public readonly record struct YpfHeader(uint Version, uint FileCount, uint HeaderSize, ReadOnlyMemory<byte> Padding = default)
{
    /// <summary>Tamanho do cabeçalho fixo; o índice começa neste offset.</summary>
    public const int Size = 0x20;

    /// <summary>Tamanho do padding no fim do cabeçalho.</summary>
    public const int PaddingSize = 16;

    private static ReadOnlySpan<byte> Magic => "YPF\0"u8;

    /// <summary>Verdadeiro quando o padding é só zeros (ou está ausente).</summary>
    public bool HasZeroPadding => Padding.Span.IndexOfAnyExcept((byte)0) < 0;

    /// <summary>Compara os campos e o conteúdo do padding (um padding ausente vale como 16 zeros).</summary>
    public bool Equals(YpfHeader other) =>
        Version == other.Version
        && FileCount == other.FileCount
        && HeaderSize == other.HeaderSize
        && PaddingEquals(Padding.Span, other.Padding.Span);

    public override int GetHashCode() => HashCode.Combine(Version, FileCount, HeaderSize);

    private static bool PaddingEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.IsEmpty || b.IsEmpty)
        {
            var other = a.IsEmpty ? b : a;
            return other.IsEmpty || other.IndexOfAnyExcept((byte)0) < 0;
        }

        return a.SequenceEqual(b);
    }

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
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            bytes.Slice(16, PaddingSize).ToArray());
        return true;
    }
}
