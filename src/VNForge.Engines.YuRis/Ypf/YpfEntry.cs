using VNForge.Core.Engines;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Uma entrada do índice YPF, com tudo o que a gravação precisa para copiar a entrada sem alterá-la
/// (regra 3 do <c>CLAUDE.md</c>: reescrita mínima).
/// </summary>
/// <param name="Path">Nome decodificado de Shift-JIS, com <c>/</c> no lugar de <c>\</c>. Único no pacote.</param>
/// <param name="Size">Tamanho do conteúdo descomprimido (<c>size_uncompressed</c>).</param>
/// <param name="IsCompressed">Verdadeiro quando o campo <c>compressed</c> vale 1 (zlib).</param>
/// <param name="NameHash">Campo <c>name_hash</c> como gravado.</param>
/// <param name="NameBytes">Bytes do nome em Shift-JIS, em claro e com o separador original; o hash do nome é calculado sobre eles.</param>
/// <param name="ObfuscatedNameBytes">Bytes do nome como estão no índice.</param>
/// <param name="EncodedNameSize">Byte <c>name_size</c> como gravado.</param>
/// <param name="NameXor">XOR efetivo que decifrou o nome desta entrada (<c>byte gravado ^ NameXor</c>).</param>
/// <param name="SwapTable">Tabela de troca que decodificou o <c>name_size</c> desta entrada.</param>
/// <param name="FileType">Campo <c>file_type</c> como gravado (o significado varia por versão).</param>
/// <param name="SizeStored">Tamanho dos bytes armazenados (<c>size_stored</c>), comprimidos quando <paramref name="IsCompressed"/>.</param>
/// <param name="Offset">Offset absoluto dos bytes armazenados. Entradas com dados idênticos podem apontar para o mesmo offset.</param>
/// <param name="DataHash">Campo <c>data_hash</c> como gravado: hash dos bytes armazenados.</param>
/// <param name="OffsetSize">Largura do campo de offset desta entrada no índice.</param>
/// <param name="IndexPosition">Posição da entrada no arquivo (início do <c>name_hash</c>).</param>
/// <param name="IndexEntrySize">Bytes que a entrada ocupa no índice.</param>
public sealed record YpfEntry(
    string Path,
    long Size,
    bool IsCompressed,
    uint NameHash,
    ReadOnlyMemory<byte> NameBytes,
    ReadOnlyMemory<byte> ObfuscatedNameBytes,
    byte EncodedNameSize,
    byte NameXor,
    YpfSwapTable SwapTable,
    byte FileType,
    uint SizeStored,
    ulong Offset,
    uint DataHash,
    YpfOffsetSize OffsetSize,
    int IndexPosition,
    int IndexEntrySize) : ArchiveEntry(Path, Size, IsCompressed)
{
    /// <summary>Chave K do nome desta entrada, no sentido do YPF-Manager (<c>~(byte ^ K)</c>).</summary>
    public byte NameKey => (byte)(NameXor ^ 0xFF);
}
