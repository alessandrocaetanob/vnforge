namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>Algoritmos de hash que um índice YPF pode usar (docs/06-yuris-notas-tecnicas.md).</summary>
public enum SyntheticYpfHashAlgorithm
{
    Adler32,
    Crc32,
    MurmurHash2,
}

/// <summary>Tabela de troca do tamanho do nome: a padrão ou a variante da v500.</summary>
public enum SyntheticYpfSwapTable
{
    Standard,
    V500,
}

/// <summary>Largura do campo de offset de cada entrada do índice.</summary>
public enum SyntheticYpfOffsetSize
{
    Bits32,
    Bits64,
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

/// <summary>Padrões por versão, conforme a tabela do doc 06.</summary>
public static class SyntheticYpfDefaults
{
    public const uint FirstCrc32Version = 265;
    public const uint FirstMurmurVersion = 479;
    public const uint V500 = 500;

    public static SyntheticYpfHashAlgorithm NameHash(uint version) =>
        version < FirstCrc32Version ? SyntheticYpfHashAlgorithm.Adler32
        : version < FirstMurmurVersion ? SyntheticYpfHashAlgorithm.Crc32
        : SyntheticYpfHashAlgorithm.MurmurHash2;

    public static SyntheticYpfHashAlgorithm DataHash(uint version) =>
        version < FirstMurmurVersion ? SyntheticYpfHashAlgorithm.Adler32 : SyntheticYpfHashAlgorithm.MurmurHash2;

    public static SyntheticYpfOffsetSize OffsetSize(uint version) =>
        version < FirstMurmurVersion ? SyntheticYpfOffsetSize.Bits32 : SyntheticYpfOffsetSize.Bits64;

    public static SyntheticYpfSwapTable SwapTable(uint version) =>
        version == V500 ? SyntheticYpfSwapTable.V500 : SyntheticYpfSwapTable.Standard;
}

/// <summary>Pares de troca do tamanho do nome (cada par vale nos dois sentidos).</summary>
public static class SyntheticYpfSwapTables
{
    private static readonly (byte A, byte B)[] StandardPairs =
    [
        (3, 72), (6, 53), (9, 11), (12, 16), (13, 19), (17, 25),
        (21, 27), (28, 30), (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    private static readonly (byte A, byte B)[] V500Pairs =
    [
        (3, 10), (6, 53), (9, 11), (12, 16), (13, 19), (17, 24),
        (20, 46), (21, 27), (28, 30), (32, 35), (38, 41), (44, 47),
    ];

    public static IReadOnlyList<(byte A, byte B)> Pairs(SyntheticYpfSwapTable table) => table switch
    {
        SyntheticYpfSwapTable.Standard => StandardPairs,
        SyntheticYpfSwapTable.V500 => V500Pairs,
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
