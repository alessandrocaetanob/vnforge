using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>Largura do campo de offset de cada entrada do índice.</summary>
public enum YpfOffsetSize
{
    Bits32,
    Bits64,
}

/// <summary>Um trecho do arquivo, por offset absoluto e tamanho em bytes.</summary>
public readonly record struct YpfByteRange(long Offset, long Length);

/// <summary>
/// O que o leitor detectou no arquivo. A gravação (#29) usa os mesmos parâmetros, para o arquivo reescrito
/// ser lido do mesmo jeito pelo jogo.
/// </summary>
/// <param name="Version">Versão gravada no cabeçalho.</param>
/// <param name="NameHash">Algoritmo do hash do nome, confirmado contra o índice.</param>
/// <param name="DataHash">
/// Algoritmo do hash dos dados, confirmado contra os dados de uma amostra de entradas. Quando a amostra não
/// distingue os algoritmos (por exemplo, só entradas vazias), vale o padrão da versão.
/// </param>
/// <param name="OffsetSize">Largura do offset, confirmada pela estrutura do índice e pelo tamanho do arquivo.</param>
/// <param name="HeaderSizeIncludesHeader">
/// Verdadeiro quando o <c>header_size</c> gravado inclui os 32 bytes do cabeçalho; falso quando conta só o índice.
/// </param>
/// <param name="SwapTable">Tabela de troca do tamanho do nome que validou o índice.</param>
/// <param name="NameXor">
/// XOR efetivo do nome: <c>byte gravado ^ NameXor</c> devolve o byte original. A chave K do doc 06 (o byte gravado
/// é <c>~(byte ^ K)</c>) é <c>NameXor ^ 0xFF</c>; é a chave que a maioria das entradas usa.
/// </param>
/// <param name="IndexSize">Bytes ocupados pelo índice (a partir do offset 0x20).</param>
/// <param name="FileLength">Tamanho do arquivo lido.</param>
/// <param name="Gaps">
/// Trechos da área de dados que nenhuma entrada referencia e que ficam antes do fim do último byte referenciado
/// (do início dos dados até a primeira entrada, e entre entradas). Os escritores conhecidos não deixam lacunas, mas um
/// YPF real pode ter; a gravação (#29) copia estes trechos do fluxo de origem, pelo offset e tamanho daqui.
/// </param>
/// <param name="Trailer">
/// Bytes depois do último byte referenciado por uma entrada até o fim do arquivo (tamanho 0 quando não há resto).
/// A gravação (#29) também os copia do fluxo de origem.
/// </param>
/// <param name="DataHashVerified">
/// Verdadeiro quando o algoritmo de <paramref name="DataHash"/> foi confirmado contra os dados de ao menos uma
/// entrada, e só por um algoritmo. Falso quando nenhuma entrada da amostra confere (ou a amostra não distingue os
/// algoritmos): vale o padrão da versão e a conferência por entrada fica com <c>OpenEntry</c> (#28).
/// </param>
public sealed record YpfParameters(
    uint Version,
    YpfHashAlgorithm NameHash,
    YpfHashAlgorithm DataHash,
    YpfOffsetSize OffsetSize,
    bool HeaderSizeIncludesHeader,
    YpfSwapTable SwapTable,
    byte NameXor,
    int IndexSize,
    long FileLength,
    IReadOnlyList<YpfByteRange> Gaps,
    YpfByteRange Trailer,
    bool DataHashVerified)
{
    /// <summary>Chave K do nome, no sentido do YPF-Manager (<c>~(byte ^ K)</c>).</summary>
    public byte NameKey => (byte)(NameXor ^ 0xFF);

    /// <summary>Offset do primeiro byte da área de dados: o cabeçalho fixo mais o índice.</summary>
    public int DataStart => YpfHeader.Size + IndexSize;

    /// <summary>O valor que o campo <c>header_size</c> deve ter para este índice, na semântica detectada.</summary>
    public uint ExpectedHeaderSize => (uint)(HeaderSizeIncludesHeader ? DataStart : IndexSize);
}
