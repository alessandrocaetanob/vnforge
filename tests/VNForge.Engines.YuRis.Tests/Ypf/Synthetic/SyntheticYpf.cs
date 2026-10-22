namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>
/// Descritor de uma entrada do YPF sintético: tudo o que o gerador escreveu, para o teste
/// conferir campo a campo o que o leitor devolve.
/// </summary>
/// <param name="Name">Nome lógico, em texto.</param>
/// <param name="NameBytes">Nome em Shift-JIS, antes do XOR. É sobre estes bytes que o hash do nome é calculado.</param>
/// <param name="ObfuscatedNameBytes">Nome como gravado no índice: cada byte vira <c>~(byte ^ K)</c>.</param>
/// <param name="EncodedNameSize">Byte <c>name_size</c> como gravado: tamanho trocado pela tabela e depois invertido (~).</param>
/// <param name="FileType">Byte <c>file_type</c>.</param>
/// <param name="IsCompressed">Byte <c>compressed</c> (1 = zlib). Só é 1 quando o zlib encolheu o conteúdo, ou quando foi forçado.</param>
/// <param name="SizeUncompressed">Tamanho do conteúdo original.</param>
/// <param name="SizeStored">Tamanho dos bytes armazenados (comprimidos, quando <paramref name="IsCompressed"/>).</param>
/// <param name="Offset">Offset absoluto dos bytes armazenados no arquivo (igual ao de outra entrada com dados compartilhados).</param>
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
    bool HeaderSizeIncludesHeader,
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

    /// <summary>Offset do primeiro byte da área de dados: o cabeçalho fixo mais o índice, qualquer que seja o valor gravado em <see cref="HeaderSize"/>.</summary>
    public uint DataStart { get; init; }

    /// <summary>XOR efetivo do nome: <c>byte gravado ^ EffectiveNameXor</c> devolve o byte original (a chave K do builder, complementada).</summary>
    public byte EffectiveNameXor => SyntheticYpfDefaults.EffectiveNameXor(NameKey);
}
