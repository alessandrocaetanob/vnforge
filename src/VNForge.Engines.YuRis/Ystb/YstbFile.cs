using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>De onde veio a chave XOR com que o leitor decifrou o YSTB.</summary>
public enum YstbKeyOrigin
{
    /// <summary>Sem argumentos para derivar a chave e as seções já estavam em claro (chave 0).</summary>
    None,

    /// <summary>
    /// Os bytes 8 a 11 da seção de argumentos, cifrados: valem como chave quando o primeiro argumento tem <c>offset = 0</c>.
    /// Também é a origem quando a derivada coincide com uma chave conhecida.
    /// </summary>
    Derived,

    /// <summary>Uma das chaves de <see cref="YstbReader.KnownKeys"/> que decifrou o arquivo e difere da derivada.</summary>
    Known,
}

/// <summary>Um argumento de instrução do YSTB, com os campos como estão no arquivo (12 bytes).</summary>
/// <param name="Id">Índice do parâmetro na lista do comando no YSCM; sem sentido em alguns comandos (docs/06).</param>
/// <param name="Type">Tipo do valor: 1 inteiro, 2 real, 3 texto (o yuri usa só os dois bits baixos, <c>ystb.py:46</c>).</param>
/// <param name="AssignOp">Operador de atribuição, 0 a 8: <c>=</c>, <c>+=</c>, <c>-=</c>, <c>*=</c>, <c>/=</c>, <c>%=</c>, <c>&amp;=</c>, <c>|=</c>, <c>^=</c>.</param>
/// <param name="Size">
/// Tamanho do trecho nos recursos. Em alguns comandos o campo tem outro sentido (valor devolvido do <c>RETURNCODE</c>,
/// posição do <c>ELSE</c>), por isso o leitor não o confere, salvo no <c>WORD</c>.
/// </param>
/// <param name="Offset">Posição do trecho nos recursos; mesma ressalva de <paramref name="Size"/>.</param>
public readonly record struct YstbArgument(ushort Id, byte Type, byte AssignOp, uint Size, uint Offset);

/// <summary>Uma instrução do YSTB, com o comando já resolvido pelo YSCM.</summary>
/// <param name="Index">Posição da instrução no script, de 0 em diante.</param>
/// <param name="Opcode">Byte gravado no arquivo; é o id do comando no YSCM.</param>
/// <param name="CommandName">Nome do comando no YSCM, por exemplo <c>WORD</c>.</param>
/// <param name="Extra">
/// Os 16 bits depois da contagem de argumentos (<c>gosub_npar</c> no yuri.md): em alguns jogos é calculado de outro jeito
/// (docs/06), então o leitor os guarda sem interpretar.
/// </param>
/// <param name="LineNumber">Linha do comando no <c>.yst</c> original, da seção de números de linha.</param>
/// <param name="FirstArgumentIndex">Posição do primeiro argumento desta instrução entre todos os argumentos do arquivo.</param>
/// <param name="Arguments">Argumentos na ordem do arquivo.</param>
public sealed record YstbInstruction(
    int Index,
    byte Opcode,
    string CommandName,
    ushort Extra,
    uint LineNumber,
    int FirstArgumentIndex,
    IReadOnlyList<YstbArgument> Arguments);

/// <summary>
/// Um script <c>yst*.ybn</c> (YSTB, versão 300 ou mais) lido e decifrado. Além das instruções, guarda o que a reescrita
/// mínima dos argumentos precisa para copiar byte a byte o que não mudou: a chave efetiva, o padding do cabeçalho e as
/// seções de argumentos e recursos em claro.
/// </summary>
public sealed class YstbFile
{
    internal YstbFile(
        string source,
        uint version,
        uint padding,
        uint key,
        YstbKeyOrigin keyOrigin,
        IReadOnlyList<YstbInstruction> instructions,
        ReadOnlyMemory<byte> argumentBytes,
        ReadOnlyMemory<byte> resources)
    {
        Source = source;
        Version = version;
        Padding = padding;
        Key = key;
        KeyOrigin = keyOrigin;
        Instructions = instructions;
        ArgumentBytes = argumentBytes;
        Resources = resources;
    }

    /// <summary>Caminho do arquivo lido, usado nas mensagens de erro.</summary>
    public string Source { get; }

    /// <summary>Versão do formato gravada no cabeçalho (300 ou mais).</summary>
    public uint Version { get; }

    /// <summary>Último campo do cabeçalho (bytes 28 a 31), normalmente 0. A gravação o copia como veio.</summary>
    public uint Padding { get; }

    /// <summary>
    /// Chave XOR efetiva, na notação do yuri.md: o primeiro byte do arquivo é o mais significativo
    /// (<c>0xD36FAC96</c> cifra com os bytes <c>D3 6F AC 96</c>). Zero quando as seções estão em claro.
    /// </summary>
    public uint Key { get; }

    public YstbKeyOrigin KeyOrigin { get; }

    /// <summary>Instruções na ordem do arquivo.</summary>
    public IReadOnlyList<YstbInstruction> Instructions { get; }

    /// <summary>Seção de argumentos em claro: 12 bytes por argumento, na ordem das instruções.</summary>
    public ReadOnlyMemory<byte> ArgumentBytes { get; }

    /// <summary>Seção de recursos em claro (textos e expressões que os argumentos apontam).</summary>
    public ReadOnlyMemory<byte> Resources { get; }

    /// <summary>Quantidade de argumentos no arquivo (<c>args_size / 12</c>).</summary>
    public int ArgumentCount => ArgumentBytes.Length / 12;

    /// <summary>Devolve os bytes que <paramref name="argument"/> aponta nos recursos.</summary>
    /// <exception cref="InvalidFileException">O trecho passa do fim dos recursos: o argumento não é do tipo que aponta para recursos.</exception>
    public ReadOnlyMemory<byte> GetResource(YstbArgument argument)
    {
        var end = (long)argument.Offset + argument.Size;
        if (end > Resources.Length)
        {
            throw new InvalidFileException(
                Source,
                $"o argumento {argument.Id} aponta para os bytes {argument.Offset.ToString(CultureInfo.InvariantCulture)} a {end.ToString(CultureInfo.InvariantCulture)} dos recursos, que têm {Resources.Length} bytes.");
        }

        return Resources.Slice((int)argument.Offset, (int)argument.Size);
    }
}
