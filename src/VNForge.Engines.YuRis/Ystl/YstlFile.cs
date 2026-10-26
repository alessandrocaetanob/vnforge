using System.Globalization;

namespace VNForge.Engines.YuRis.Ystl;

/// <summary>Um script da lista do <c>yst_list.ybn</c> (YSTL).</summary>
/// <param name="Index">Índice do script; também o número no nome do YSTB (<c>yst00012.ybn</c> é o índice 12).</param>
/// <param name="Path">Caminho original do <c>.yst</c> que gerou o YSTB (por exemplo <c>userscript\01_prologo.yst</c>), em Shift-JIS no arquivo.</param>
/// <param name="ModifiedFileTime">Data de modificação do <c>.yst</c> como <c>FILETIME</c> do Windows (100 ns desde 1601-01-01 UTC), crua.</param>
/// <param name="VariableCount">Variáveis definidas no script. Negativo quando não existe YSTB para o script (<c>global.yst</c> ou script vazio).</param>
/// <param name="LabelCount">Rótulos definidos no script.</param>
/// <param name="TextCount">Comandos <c>RETURNCODE</c> do script. Nulo nos arquivos sem esse campo (<see cref="YstlFile.HasTextCount"/>).</param>
public sealed record YstlScript(
    uint Index,
    string Path,
    ulong ModifiedFileTime,
    int VariableCount,
    uint LabelCount,
    uint? TextCount)
{
    /// <summary>Falso quando o compilador não gerou YSTB para este script.</summary>
    public bool HasCompiledFile => VariableCount >= 0;

    /// <summary>Nome do YSTB dentro do jogo: <c>yst</c>, o índice com cinco dígitos e <c>.ybn</c>.</summary>
    public string BinaryFileName => string.Create(CultureInfo.InvariantCulture, $"yst{Index:D5}.ybn");

    /// <summary>
    /// Nome para mostrar ao usuário: o arquivo do caminho original, sem pasta e sem extensão
    /// (<c>userscript\01_prologo.yst</c> vira <c>01_prologo</c>). Aceita <c>\</c> e <c>/</c>.
    /// </summary>
    public string ReadableName
    {
        get
        {
            var start = Path.AsSpan().LastIndexOfAny('\\', '/') + 1;
            var name = Path.AsSpan(start);
            var dot = name.LastIndexOf('.');
            return (dot > 0 ? name[..dot] : name).ToString();
        }
    }

    /// <summary>
    /// A pasta do caminho original, com <c>/</c> (<c>userscript\01_cap1\manha.yst</c> vira <c>userscript/01_cap1</c>); vazia quando o caminho é só o arquivo.
    /// </summary>
    public string Folder
    {
        get
        {
            var end = Path.AsSpan().LastIndexOfAny('\\', '/');
            return end <= 0 ? string.Empty : Path.AsSpan(0, end).ToString().Replace('\\', '/');
        }
    }

    /// <summary>Data de modificação em UTC, ou nulo se o <c>FILETIME</c> está fora do intervalo de <see cref="DateTime"/>.</summary>
    public DateTime? ModifiedUtc =>
        ModifiedFileTime <= (ulong)DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc((long)ModifiedFileTime) : null;
}

/// <summary>
/// O <c>yst_list.ybn</c> (YSTL) lido: um registro por script, na ordem do índice. Serve para dar nome legível
/// aos <c>ystNNNNN.ybn</c>.
/// </summary>
public sealed class YstlFile
{
    private readonly Dictionary<uint, YstlScript> _byIndex;

    internal YstlFile(string source, uint version, bool hasTextCount, IReadOnlyList<YstlScript> scripts)
    {
        Source = source;
        Version = version;
        HasTextCount = hasTextCount;
        Scripts = scripts;
        _byIndex = scripts.ToDictionary(s => s.Index);
    }

    /// <summary>Caminho do arquivo lido, usado nas mensagens de erro.</summary>
    public string Source { get; }

    /// <summary>Versão do formato gravada no cabeçalho.</summary>
    public uint Version { get; }

    /// <summary>Verdadeiro quando cada registro termina com o campo de contagem de textos (<c>txt_count</c>), conforme o conteúdo do arquivo.</summary>
    public bool HasTextCount { get; }

    /// <summary>Scripts na ordem do arquivo: <c>Scripts[i].Index == i</c>.</summary>
    public IReadOnlyList<YstlScript> Scripts { get; }

    /// <summary>Procura um script pelo índice.</summary>
    public bool TryGetByIndex(uint index, out YstlScript script) => _byIndex.TryGetValue(index, out script!);

    /// <summary>
    /// Procura o script de um YSTB pelo nome do arquivo (<c>yst00012.ybn</c>, sem pasta, sem diferenciar caixa).
    /// Falso para outro formato de nome, como <c>ysc.ybn</c> ou <c>yst_list.ybn</c>.
    /// </summary>
    public bool TryGetByBinaryName(string fileName, out YstlScript script)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        const string prefix = "yst";
        const string suffix = ".ybn";
        if (fileName.Length > prefix.Length + suffix.Length
            && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(
                fileName.AsSpan(prefix.Length, fileName.Length - prefix.Length - suffix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index))
        {
            return TryGetByIndex(index, out script);
        }

        script = null!;
        return false;
    }
}
