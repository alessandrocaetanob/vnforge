namespace VNForge.Engines.YuRis.Yslb;

/// <summary>Um rótulo (<c>#NOME</c>) da lista do <c>ysl.ybn</c> (YSLB).</summary>
/// <param name="Name">Nome do rótulo, em Shift-JIS no arquivo.</param>
/// <param name="CodePosition">Índice da instrução do YSTB onde o rótulo começa (versões 300 ou mais).</param>
/// <param name="ScriptId">Índice do script na lista do <c>yst_list.ybn</c>: o rótulo está em <c>yst&lt;ScriptId com 5 dígitos&gt;.ybn</c>.</param>
/// <param name="IfLevel">Nível de aninhamento de <c>IF</c> onde o rótulo foi declarado.</param>
/// <param name="LoopLevel">Nível de aninhamento de <c>LOOP</c> onde o rótulo foi declarado.</param>
public sealed record YslbLabel(string Name, uint CodePosition, ushort ScriptId, byte IfLevel, byte LoopLevel)
{
    /// <summary>Nome do YSTB que declara o rótulo, o mesmo de <see cref="Ystl.YstlScript.BinaryFileName"/>.</summary>
    public string ScriptFileName => $"yst{ScriptId:D5}.ybn";
}

/// <summary>
/// O <c>ysl.ybn</c> (YSLB) lido: todos os rótulos de todos os scripts do jogo. Serve para saber em que script cai um <c>GO</c>
/// ou um <c>GOSUB</c> que nomeia um rótulo.
/// </summary>
public sealed class YslbFile
{
    private readonly Dictionary<string, List<YslbLabel>> _exact;
    private readonly Dictionary<string, List<YslbLabel>> _ignoringCase;

    internal YslbFile(string source, uint version, IReadOnlyList<YslbLabel> labels)
    {
        Source = source;
        Version = version;
        Labels = labels;
        _exact = Group(labels, StringComparer.Ordinal);
        _ignoringCase = Group(labels, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Caminho do arquivo lido, usado nas mensagens de erro.</summary>
    public string Source { get; }

    /// <summary>Versão do formato gravada no cabeçalho.</summary>
    public uint Version { get; }

    /// <summary>Rótulos na ordem do arquivo (ordenados pelo hash do nome, não pelo script).</summary>
    public IReadOnlyList<YslbLabel> Labels { get; }

    /// <summary>
    /// Acha o rótulo de <paramref name="name"/>. Vale o nome com a mesma caixa; sem ele, vale o único script que declara o nome
    /// ignorando a caixa. Nulo se não existe ou se o nome cai em mais de um script (o jogo de referência tem
    /// <c>ES.SAVE.END</c> e <c>es.Save.end</c> em scripts diferentes), porque então não dá para saber qual vale.
    /// </summary>
    public YslbLabel? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Pick(_exact, name) ?? Pick(_ignoringCase, name);
    }

    private static YslbLabel? Pick(Dictionary<string, List<YslbLabel>> index, string name)
    {
        if (!index.TryGetValue(name, out var candidates))
        {
            return null;
        }

        return candidates.All(c => c.ScriptId == candidates[0].ScriptId) ? candidates[0] : null;
    }

    private static Dictionary<string, List<YslbLabel>> Group(IReadOnlyList<YslbLabel> labels, StringComparer comparer)
    {
        var index = new Dictionary<string, List<YslbLabel>>(comparer);
        foreach (var label in labels)
        {
            if (!index.TryGetValue(label.Name, out var list))
            {
                index[label.Name] = list = [];
            }

            list.Add(label);
        }

        return index;
    }
}
