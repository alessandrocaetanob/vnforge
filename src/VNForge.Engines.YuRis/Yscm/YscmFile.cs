using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Yscm;

/// <summary>Um parâmetro de um comando do YSCM: o nome e os dois bytes que o yuri.md ainda não explica.</summary>
/// <param name="Name">Nome do parâmetro, em Shift-JIS no arquivo.</param>
/// <param name="TypeCode">
/// Primeiro byte depois do nome. O yuri lê como <c>Typ</c> (<c>yscm.py:17-18</c>); o yuri.md o chama de desconhecido.
/// </param>
/// <param name="Flags">Segundo byte depois do nome, desconhecido nas duas fontes.</param>
public readonly record struct YscmArgument(string Name, byte TypeCode, byte Flags);

/// <summary>Um comando do YSCM. O <see cref="Id"/> é a posição na lista e é o opcode que o YSTB grava.</summary>
/// <param name="Id">Posição do comando na lista (0 a 255); o byte <c>opcode</c> de cada instrução do YSTB.</param>
/// <param name="Name">Nome do comando, por exemplo <c>WORD</c>.</param>
/// <param name="Arguments">Parâmetros na ordem do arquivo; a posição é o <c>arg_id</c> do YSTB.</param>
public sealed record YscmCommand(byte Id, string Name, IReadOnlyList<YscmArgument> Arguments);

/// <summary>Os opcodes dos cinco comandos que o extrator de texto procura (docs/06-yuris-notas-tecnicas.md).</summary>
/// <param name="Word"><c>WORD</c>: texto do diálogo.</param>
/// <param name="Expression"><c>_</c>: expressão solta, que pode carregar string.</param>
/// <param name="ReturnCode"><c>RETURNCODE</c>: fim da linha de texto, cede o controle.</param>
/// <param name="Gosub"><c>GOSUB</c>: chamada de rotina (<c>ES.SEL.SET</c>, <c>ES.CHAR.NAME</c>).</param>
/// <param name="Cg"><c>CG</c>: exibição de imagem.</param>
public readonly record struct YuRisOpcodes(byte Word, byte Expression, byte ReturnCode, byte Gosub, byte Cg)
{
    public const string WordName = "WORD";
    public const string ExpressionName = "_";
    public const string ReturnCodeName = "RETURNCODE";
    public const string GosubName = "GOSUB";
    public const string CgName = "CG";
}

/// <summary>
/// O <c>ysc.ybn</c> (YSCM) lido: a lista de comandos na ordem do id. Quem lê um YSTB pergunta aqui o opcode
/// de cada comando, porque o número muda de jogo para jogo.
/// </summary>
public sealed class YscmFile
{
    internal YscmFile(string source, uint version, IReadOnlyList<YscmCommand> commands, int errorMessageCount)
    {
        Source = source;
        Version = version;
        Commands = commands;
        ErrorMessageCount = errorMessageCount;
    }

    /// <summary>Caminho do arquivo lido, usado nas mensagens de erro.</summary>
    public string Source { get; }

    /// <summary>Versão do formato gravada no cabeçalho.</summary>
    public uint Version { get; }

    /// <summary>Comandos na ordem do id: <c>Commands[i].Id == i</c>.</summary>
    public IReadOnlyList<YscmCommand> Commands { get; }

    /// <summary>
    /// Mensagens de erro entre o último comando e a tabela final de 256 bytes: 37 ou 0x90 (as fontes divergem, docs/06).
    /// O leitor as lê como strings para conferir que a cauda fecha, mas não guarda o texto.
    /// </summary>
    public int ErrorMessageCount { get; }

    /// <summary>Procura o opcode de um comando pelo nome (comparação exata, com caixa).</summary>
    /// <returns>Falso se nenhum comando tem esse nome.</returns>
    /// <exception cref="InvalidFileException">Mais de um comando tem o nome, então o opcode é ambíguo.</exception>
    public bool TryGetOpcode(string name, out byte opcode)
    {
        ArgumentNullException.ThrowIfNull(name);
        byte? found = null;
        var count = 0;
        foreach (var command in Commands)
        {
            if (string.Equals(command.Name, name, StringComparison.Ordinal))
            {
                found ??= command.Id;
                count++;
            }
        }

        if (count > 1)
        {
            var ids = string.Join(", ", Commands.Where(c => c.Name == name).Select(c => c.Id.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidFileException(Source, $"o comando '{name}' aparece {count} vezes no YSCM (ids {ids}), então o opcode é ambíguo.");
        }

        opcode = found.GetValueOrDefault();
        return found.HasValue;
    }

    /// <summary>Devolve o opcode de um comando que o chamador precisa ter.</summary>
    /// <exception cref="InvalidFileException">O comando não está no YSCM (a mensagem nomeia o comando) ou o nome é ambíguo.</exception>
    public byte GetOpcode(string name) =>
        TryGetOpcode(name, out var opcode)
            ? opcode
            : throw MissingCommands([name]);

    /// <summary>Devolve os opcodes de <c>WORD</c>, <c>_</c>, <c>RETURNCODE</c>, <c>GOSUB</c> e <c>CG</c>.</summary>
    /// <exception cref="InvalidFileException">Falta algum dos cinco (a mensagem nomeia todos os que faltam) ou algum é ambíguo.</exception>
    public YuRisOpcodes GetOpcodes()
    {
        var missing = new List<string>();
        var word = Find(YuRisOpcodes.WordName, missing);
        var expression = Find(YuRisOpcodes.ExpressionName, missing);
        var returnCode = Find(YuRisOpcodes.ReturnCodeName, missing);
        var gosub = Find(YuRisOpcodes.GosubName, missing);
        var cg = Find(YuRisOpcodes.CgName, missing);
        if (missing.Count > 0)
        {
            throw MissingCommands(missing);
        }

        return new YuRisOpcodes(word, expression, returnCode, gosub, cg);
    }

    private byte Find(string name, List<string> missing)
    {
        if (TryGetOpcode(name, out var opcode))
        {
            return opcode;
        }

        missing.Add(name);
        return 0;
    }

    private InvalidFileException MissingCommands(IReadOnlyList<string> names)
    {
        var quoted = string.Join(", ", names.Select(n => $"'{n}'"));
        var subject = names.Count == 1
            ? $"o comando esperado {quoted} não está"
            : $"os comandos esperados {quoted} não estão";
        return new InvalidFileException(
            Source,
            $"{subject} na lista do YSCM (versão {Version}, {Commands.Count} comandos); sem ele não dá para achar o opcode nos scripts.");
    }
}
