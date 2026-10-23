namespace VNForge.Engines.YuRis.Ystb;

/// <summary>O que o argumento de uma instrução carrega, para o despejo mostrar cada um do jeito certo.</summary>
public enum YstbArgumentKind
{
    /// <summary>Argumento do <c>WORD</c>: bytes de texto em Shift-JIS, mostrados com os marcadores de controle.</summary>
    Text,

    /// <summary>Expressão que é só uma string literal; o valor é o texto com marcadores, sem as aspas.</summary>
    String,

    /// <summary>Qualquer outra expressão; o valor são os bytes dela em hexadecimal.</summary>
    Expression,

    /// <summary>
    /// Argumento cujo <c>size</c> e <c>offset</c> não apontam para os recursos (<c>RETURNCODE</c>, 2º e 3º de <c>IF</c>/<c>ELSE</c>
    /// com 3 argumentos, 2º de <c>LOOP</c>); só o par <c>size</c>/<c>offset</c> tem sentido.
    /// </summary>
    Value,

    /// <summary>Argumento sem bytes nos recursos (<c>size = 0</c>).</summary>
    Empty,
}

/// <summary>Um argumento de instrução no despejo.</summary>
/// <param name="Index">Posição do argumento na instrução, de 0 em diante.</param>
/// <param name="Id">Campo <c>id</c> do arquivo; indexa os parâmetros do comando no YSCM.</param>
/// <param name="Name">Nome do parâmetro no YSCM; nulo se o <paramref name="Id"/> passa da lista do comando.</param>
/// <param name="Type">Campo <c>type</c> cru (1 inteiro, 2 real, 3 texto).</param>
/// <param name="AssignOp">Campo <c>assign_op</c> cru.</param>
/// <param name="AssignOperator">Operador de atribuição (<c>=</c>, <c>+=</c> e os demais); o número se estiver fora de 0 a 8.</param>
/// <param name="Size">Campo <c>size</c> cru.</param>
/// <param name="Offset">Campo <c>offset</c> cru.</param>
/// <param name="Kind">O que o argumento carrega.</param>
/// <param name="Value">Texto com marcadores (<see cref="YstbArgumentKind.Text"/> e <see cref="YstbArgumentKind.String"/>), hexadecimal (<see cref="YstbArgumentKind.Expression"/>) ou nulo.</param>
public sealed record YstbDumpArgument(
    int Index,
    ushort Id,
    string? Name,
    byte Type,
    byte AssignOp,
    string AssignOperator,
    uint Size,
    uint Offset,
    YstbArgumentKind Kind,
    string? Value);

/// <summary>Uma instrução no despejo.</summary>
/// <param name="Index">Posição da instrução no script, de 0 em diante.</param>
/// <param name="LineNumber">Linha do comando no <c>.yst</c> original.</param>
/// <param name="Opcode">Byte gravado no arquivo; o id do comando no YSCM.</param>
/// <param name="Command">Nome do comando no YSCM.</param>
/// <param name="Extra">Os 16 bits depois da contagem de argumentos, sem interpretação.</param>
/// <param name="Arguments">Argumentos na ordem do arquivo.</param>
public sealed record YstbDumpInstruction(
    int Index,
    uint LineNumber,
    byte Opcode,
    string Command,
    ushort Extra,
    IReadOnlyList<YstbDumpArgument> Arguments);

/// <summary>Um script YSTB em forma legível: comandos, argumentos e textos com marcadores.</summary>
/// <param name="Source">Caminho do arquivo lido.</param>
/// <param name="Version">Versão do formato gravada no cabeçalho.</param>
/// <param name="Key">Chave XOR efetiva; zero quando as seções estão em claro.</param>
/// <param name="KeyOrigin">De onde veio a chave.</param>
/// <param name="ArgumentCount">Quantidade de argumentos no arquivo.</param>
/// <param name="Instructions">Instruções na ordem do arquivo.</param>
public sealed record YstbDump(
    string Source,
    uint Version,
    uint Key,
    YstbKeyOrigin KeyOrigin,
    int ArgumentCount,
    IReadOnlyList<YstbDumpInstruction> Instructions);
