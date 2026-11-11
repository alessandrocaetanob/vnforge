using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>
/// Defeitos pontuais num YPF sintético, para os testes negativos do leitor e do escritor. Cada método devolve uma
/// cópia dos bytes e deixa o resto do arquivo intacto; o descritor continua descrevendo o arquivo original.
/// </summary>
public static class SyntheticYpfMutations
{
    private const int HeaderSizePosition = 12;

    /// <summary>Inverte o bit menos significativo do <c>name_hash</c> de uma entrada.</summary>
    public static byte[] WithWrongNameHash(this SyntheticYpf ypf, string entryName)
    {
        var entry = Find(ypf, entryName);
        var bytes = (byte[])ypf.Bytes.Clone();
        bytes[entry.IndexPosition] ^= 0x01;
        return bytes;
    }

    /// <summary>Inverte o bit menos significativo do <c>data_hash</c> de uma entrada (os dados continuam corretos).</summary>
    public static byte[] WithWrongDataHash(this SyntheticYpf ypf, string entryName)
    {
        var entry = Find(ypf, entryName);
        var bytes = (byte[])ypf.Bytes.Clone();
        bytes[entry.IndexPosition + entry.IndexEntrySize - 4] ^= 0x01;
        return bytes;
    }

    /// <summary>Troca o <c>size_uncompressed</c> de uma entrada (os dados e o hash continuam corretos).</summary>
    public static byte[] WithUncompressedSize(this SyntheticYpf ypf, string entryName, uint size)
    {
        var entry = Find(ypf, entryName);
        var offsetBytes = ypf.OffsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;
        var bytes = (byte[])ypf.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry.IndexPosition + entry.IndexEntrySize - 4 - offsetBytes - 8), size);
        return bytes;
    }

    /// <summary>Troca o <c>size_stored</c> de uma entrada (o hash passa a divergir dos bytes que o índice delimita).</summary>
    public static byte[] WithStoredSize(this SyntheticYpf ypf, string entryName, uint size)
    {
        var entry = Find(ypf, entryName);
        var offsetBytes = ypf.OffsetSize == SyntheticYpfOffsetSize.Bits64 ? 8 : 4;
        var bytes = (byte[])ypf.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry.IndexPosition + entry.IndexEntrySize - 4 - offsetBytes - 4), size);
        return bytes;
    }

    /// <summary>Troca o valor gravado em <c>header_size</c>.</summary>
    public static byte[] WithHeaderSize(this SyntheticYpf ypf, uint headerSize)
    {
        var bytes = (byte[])ypf.Bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(HeaderSizePosition), headerSize);
        return bytes;
    }

    private static SyntheticYpfEntry Find(SyntheticYpf ypf, string entryName) =>
        ypf.Entries.SingleOrDefault(e => e.Name == entryName)
        ?? throw new ArgumentException($"Não há entrada chamada '{entryName}'.", nameof(entryName));
}
