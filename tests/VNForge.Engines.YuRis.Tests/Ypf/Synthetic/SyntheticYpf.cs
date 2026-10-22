namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>
/// Descritor de uma entrada do YPF sintético: tudo o que o gerador escreveu, para o teste
/// conferir campo a campo o que o leitor devolve.
/// </summary>
/// <param name="Name">Nome lógico, em texto.</param>
/// <param name="NameBytes">Nome em Shift-JIS, antes do XOR. É sobre estes bytes que o hash do nome é calculado.</param>
/// <param name="ObfuscatedNameBytes">Nome como gravado no índice (cada byte com XOR da chave).</param>
/// <param name="EncodedNameSize">Byte <c>name_size</c> como gravado: tamanho trocado pela tabela e depois invertido (~).</param>
/// <param name="FileType">Byte <c>file_type</c>.</param>
/// <param name="IsCompressed">Byte <c>compressed</c> (1 = zlib).</param>
/// <param name="SizeUncompressed">Tamanho do conteúdo original.</param>
/// <param name="SizeStored">Tamanho dos bytes armazenados (comprimidos, quando <paramref name="IsCompressed"/>).</param>
/// <param name="Offset">Offset absoluto dos bytes armazenados no arquivo.</param>
/// <param name="NameHash">Hash do nome (<see cref="SyntheticYpf.NameHashAlgorithm"/>).</param>
/// <param name="DataHash">Hash dos bytes armazenados (<see cref="SyntheticYpf.DataHashAlgorithm"/>).</param>
/// <param name="IndexPosition">Posição da entrada dentro do arquivo.</param>
/// <param name="IndexEntrySize">Quantidade de bytes da entrada no índice.</param>
/// <param name="Content">Conteúdo original.</param>
/// <param name="StoredBytes">Bytes gravados na área de dados.</param>
public sealed record SyntheticYpfEntry(
    string Name,
    byte[] NameBytes,
    byte[] ObfuscatedNameBytes,
    byte EncodedNameSize,
    SyntheticYpfFileType FileType,
    bool IsCompressed,
    uint SizeUncompressed,
    uint SizeStored,
    ulong Offset,
    uint NameHash,
    uint DataHash,
    int IndexPosition,
    int IndexEntrySize,
    byte[] Content,
    byte[] StoredBytes);

/// <summary>Resultado do <see cref="SyntheticYpfBuilder"/>: os bytes do arquivo e o descritor de cada entrada, na ordem do índice.</summary>
public sealed record SyntheticYpf(
    byte[] Bytes,
    uint Version,
    uint HeaderSize,
    byte NameKey,
    SyntheticYpfSwapTable SwapTable,
    SyntheticYpfHashAlgorithm NameHashAlgorithm,
    SyntheticYpfHashAlgorithm DataHashAlgorithm,
    SyntheticYpfOffsetSize OffsetSize,
    IReadOnlyList<SyntheticYpfEntry> Entries)
{
    /// <summary>Tamanho do cabeçalho fixo (magic, versão, contagem, header_size e 16 bytes de padding); o índice começa aqui.</summary>
    public const int FixedHeaderSize = 0x20;

    public uint FileCount => (uint)Entries.Count;
}
