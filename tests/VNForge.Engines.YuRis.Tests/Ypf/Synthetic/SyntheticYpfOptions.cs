using System.Collections.Immutable;

namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>Algoritmos de hash que um índice YPF pode usar (docs/06-yuris-notas-tecnicas.md).</summary>
public enum SyntheticYpfHashAlgorithm
{
    Adler32,
    Crc32,
    MurmurHash2,
}

/// <summary>
/// Tabela de troca do tamanho do nome. <see cref="Standard"/> e <see cref="V500"/> são as de yuri.py e do
/// YPF-Manager; <see cref="Garbro04"/> e <see cref="Garbro10"/> são as reduzidas que o GARbro usa em
/// versões antigas (docs/06, "Tabela de troca do tamanho do nome").
/// </summary>
public enum SyntheticYpfSwapTable
{
    Standard,
    V500,
    Garbro04,
    Garbro10,
}

/// <summary>Largura do campo de offset de cada entrada do índice.</summary>
public enum SyntheticYpfOffsetSize
{
    Bits32,
    Bits64,
}

/// <summary>Em que ordem os bytes armazenados vão para a área de dados.</summary>
public enum SyntheticYpfDataOrder
{
    /// <summary>Na ordem do índice (hash do nome crescente), como GARbro, YPF-Manager e yuri.py.</summary>
    Index,

    /// <summary>Na ordem de <see cref="SyntheticYpfBuilder.AddFile"/>; os offsets deixam de ser crescentes no índice.</summary>
    Insertion,
}

/// <summary>Valores de <c>file_type</c> da entrada.</summary>
public enum SyntheticYpfFileType : byte
{
    Ybn = 0,
    Bmp = 1,
    Png = 2,
    Jpg = 3,
    Gif = 4,
    Wav = 5,
    Ogg = 6,
    Psd = 7,
    Ycg = 8,
    Psb = 9,
}

/// <summary>Padrões por versão (docs/06, com a fonte de cada um).</summary>
public static class SyntheticYpfDefaults
{
    public const uint FirstCrc32Version = 265;

    /// <summary>
    /// Primeira versão com MurmurHash2 e offset de 64 bits na leitura do doc 06 (YPF-Manager, YPFHeader.cs:130).
    /// yuri.py:55 usa 477; o limiar real nunca foi medido (GARbro#452 mediu 474 e 479).
    /// </summary>
    public const uint FirstMurmurVersion = 479;

    public const uint V290 = 290;
    public const uint FirstHeaderSizeWithHeaderVersion = 300;
    public const uint V500 = 500;

    public static SyntheticYpfHashAlgorithm NameHash(uint version)
    {
        if (version < FirstCrc32Version)
        {
            return SyntheticYpfHashAlgorithm.Adler32;
        }

        return version < FirstMurmurVersion ? SyntheticYpfHashAlgorithm.Crc32 : SyntheticYpfHashAlgorithm.MurmurHash2;
    }

    public static SyntheticYpfHashAlgorithm DataHash(uint version) =>
        version < FirstMurmurVersion ? SyntheticYpfHashAlgorithm.Adler32 : SyntheticYpfHashAlgorithm.MurmurHash2;

    public static SyntheticYpfOffsetSize OffsetSize(uint version) =>
        version < FirstMurmurVersion ? SyntheticYpfOffsetSize.Bits32 : SyntheticYpfOffsetSize.Bits64;

    /// <summary>Leitura de yuri.py:78: a tabela da v500 vale só para a versão 500 exata (o YPF-Manager usa 500 ou mais).</summary>
    public static SyntheticYpfSwapTable SwapTable(uint version) =>
        version == V500 ? SyntheticYpfSwapTable.V500 : SyntheticYpfSwapTable.Standard;

    /// <summary>
    /// A chave K do nome, no sentido do YPF-Manager: o byte gravado é <c>~(byte ^ K)</c>. Padrão 0x00, 0x40 na v290 e
    /// 0x36 na v500 (yuri.py:79, que usa a v500 exata; o YPF-Manager usa 500 ou mais, YPFHeader.cs:146-158).
    /// </summary>
    public static byte NameKey(uint version) => version switch
    {
        V290 => 0x40,
        V500 => 0x36,
        _ => 0x00,
    };

    /// <summary>
    /// Abaixo da v300 o <c>header_size</c> não inclui os 32 bytes do cabeçalho (yuri.md:459-462, ypf.py:91 e :137).
    /// GARbro e YPF-Manager gravam sempre com eles.
    /// </summary>
    public static bool HeaderSizeIncludesHeader(uint version) => version >= FirstHeaderSizeWithHeaderVersion;

    /// <summary>XOR efetivo de um byte do nome gravado: <c>stored ^ EffectiveXor == plain</c>, ou seja, <c>K ^ 0xFF</c>.</summary>
    public static byte EffectiveNameXor(byte key) => (byte)(key ^ 0xFF);
}

/// <summary>Pares de troca do tamanho do nome (cada par vale nos dois sentidos).</summary>
public static class SyntheticYpfSwapTables
{
    private static readonly ImmutableArray<(byte A, byte B)> StandardPairs =
    [
        (3, 72), (6, 53), (9, 11), (12, 16), (13, 19), (17, 25),
        (21, 27), (28, 30), (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    private static readonly ImmutableArray<(byte A, byte B)> V500Pairs =
    [
        (3, 10), (6, 53), (9, 11), (12, 16), (13, 19), (17, 24),
        (20, 46), (21, 27), (28, 30), (32, 35), (38, 41), (44, 47),
    ];

    // GARbro ArcYPF.cs:535-538 (SwapTable04): sem 3<->72 e 6<->53.
    private static readonly ImmutableArray<(byte A, byte B)> Garbro04Pairs =
    [
        (12, 16), (17, 25), (28, 30), (9, 11), (13, 19), (21, 27),
        (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    // GARbro ArcYPF.cs:539-541 (SwapTable10): só os pares 9, 13, 21, 32, 38, 44 e 46.
    private static readonly ImmutableArray<(byte A, byte B)> Garbro10Pairs =
    [
        (9, 11), (13, 19), (21, 27), (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    public static IReadOnlyList<(byte A, byte B)> Pairs(SyntheticYpfSwapTable table) => table switch
    {
        SyntheticYpfSwapTable.Standard => StandardPairs,
        SyntheticYpfSwapTable.V500 => V500Pairs,
        SyntheticYpfSwapTable.Garbro04 => Garbro04Pairs,
        SyntheticYpfSwapTable.Garbro10 => Garbro10Pairs,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    /// <summary>Troca o valor pelo seu par; valores fora da tabela passam sem mudança. A troca é uma involução.</summary>
    public static byte Swap(SyntheticYpfSwapTable table, byte value)
    {
        foreach (var (a, b) in Pairs(table))
        {
            if (value == a)
            {
                return b;
            }

            if (value == b)
            {
                return a;
            }
        }

        return value;
    }
}
