using System.Collections.Immutable;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// Tabela de troca do tamanho do nome. <see cref="Standard"/> e <see cref="V500"/> são as de yuri.md e do
/// YPF-Manager; <see cref="Garbro04"/> e <see cref="Garbro10"/> são as reduzidas que o GARbro usa em
/// versões antigas (docs/06-yuris-notas-tecnicas.md, "Tabela de troca do tamanho do nome").
/// </summary>
public enum YpfSwapTable
{
    Standard,
    V500,
    Garbro04,
    Garbro10,
}

/// <summary>Os pares de troca de cada <see cref="YpfSwapTable"/>; cada par vale nos dois sentidos.</summary>
public static class YpfSwapTables
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

    private static readonly ImmutableArray<(byte A, byte B)> Garbro04Pairs =
    [
        (12, 16), (17, 25), (28, 30), (9, 11), (13, 19), (21, 27),
        (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    private static readonly ImmutableArray<(byte A, byte B)> Garbro10Pairs =
    [
        (9, 11), (13, 19), (21, 27), (32, 35), (38, 41), (44, 47), (46, 50),
    ];

    /// <summary>As tabelas conhecidas, na ordem em que o leitor as tenta quando a versão não indica uma.</summary>
    public static IReadOnlyList<YpfSwapTable> All { get; } =
        [YpfSwapTable.Standard, YpfSwapTable.V500, YpfSwapTable.Garbro04, YpfSwapTable.Garbro10];

    public static IReadOnlyList<(byte A, byte B)> Pairs(YpfSwapTable table) => table switch
    {
        YpfSwapTable.Standard => StandardPairs,
        YpfSwapTable.V500 => V500Pairs,
        YpfSwapTable.Garbro04 => Garbro04Pairs,
        YpfSwapTable.Garbro10 => Garbro10Pairs,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    /// <summary>Troca o valor pelo seu par; valores fora da tabela passam sem mudança. A troca é uma involução.</summary>
    public static byte Swap(YpfSwapTable table, byte value)
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

    /// <summary>Tamanho do nome a partir do byte <c>name_size</c> gravado: inverte (~) e depois aplica a troca.</summary>
    public static byte DecodeNameSize(YpfSwapTable table, byte stored) => Swap(table, (byte)~stored);
}
