using System.Buffers.Binary;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Tests.Yscm.Synthetic;

namespace VNForge.Engines.YuRis.Tests.Ystb.Synthetic;

/// <summary>Argumento de uma instrução do YSTB sintético, com os campos crus do formato.</summary>
public readonly record struct SyntheticYstbArgument(ushort Id, byte Type, byte AssignOp, uint Size, uint Offset);

/// <summary>Instrução do YSTB sintético: o que o leitor deve devolver.</summary>
public sealed record SyntheticYstbInstruction(
    byte Opcode,
    ushort Extra,
    uint LineNumber,
    IReadOnlyList<SyntheticYstbArgument> Arguments);

/// <summary>Resultado do <see cref="SyntheticYstbBuilder"/>: os bytes do arquivo e as seções em claro.</summary>
public sealed record SyntheticYstb(
    byte[] Bytes,
    uint Version,
    uint Key,
    uint Padding,
    IReadOnlyList<SyntheticYstbInstruction> Instructions,
    byte[] PlainArguments,
    byte[] PlainResources);

/// <summary>
/// Monta um <c>yst*.ybn</c> (YSTB) sintético a partir da versão 300: cabeçalho de 32 bytes, instruções, argumentos,
/// recursos e números de linha, cada seção cifrada com XOR de 4 bytes que recomeça no início da seção
/// (docs/06-yuris-notas-tecnicas.md). Os opcodes vêm de um YSCM sintético, para o leitor não depender de números fixos.
/// </summary>
public sealed class SyntheticYstbBuilder(SyntheticYscm yscm)
{
    public const uint KeyV290 = 0xD36FAC96;
    public const uint KeyV200 = 0x07B4024A;

    private readonly List<SyntheticYstbInstruction> _instructions = [];
    private readonly MemoryStream _resources = new();
    private uint _version = 500;
    private uint _key = KeyV290;
    private uint _padding;

    public SyntheticYstbBuilder WithVersion(uint version)
    {
        _version = version;
        return this;
    }

    /// <summary>Chave na notação do yuri.md: o primeiro byte do arquivo é o mais significativo. Zero deixa tudo em claro.</summary>
    public SyntheticYstbBuilder WithKey(uint key)
    {
        _key = key;
        return this;
    }

    public SyntheticYstbBuilder WithPadding(uint padding)
    {
        _padding = padding;
        return this;
    }

    /// <summary>Acrescenta bytes no fim dos recursos, sem argumento que aponte para eles.</summary>
    public SyntheticYstbBuilder AddResource(byte[] bytes)
    {
        _resources.Write(bytes);
        return this;
    }

    /// <summary>Argumento que aponta para <paramref name="bytes"/> recém-acrescentados aos recursos.</summary>
    public SyntheticYstbArgument ResourceArgument(byte[] bytes, ushort id = 0, byte type = 3, byte assignOp = 0)
    {
        var offset = (uint)_resources.Length;
        _resources.Write(bytes);
        return new SyntheticYstbArgument(id, type, assignOp, (uint)bytes.Length, offset);
    }

    public SyntheticYstbBuilder AddInstruction(byte opcode, ushort extra, uint lineNumber, params SyntheticYstbArgument[] arguments)
    {
        _instructions.Add(new SyntheticYstbInstruction(opcode, extra, lineNumber, arguments));
        return this;
    }

    /// <summary>Instrução do comando <paramref name="commandName"/> do YSCM, com os argumentos dados.</summary>
    public SyntheticYstbBuilder AddCommand(string commandName, uint lineNumber, params SyntheticYstbArgument[] arguments) =>
        AddInstruction(yscm.IdOf(commandName), 0, lineNumber, arguments);

    /// <summary>Instrução <c>WORD</c> com o texto em Shift-JIS nos recursos.</summary>
    public SyntheticYstbBuilder AddWord(string text, uint lineNumber) =>
        AddCommand("WORD", lineNumber, ResourceArgument(GameEncodings.ShiftJis.GetBytes(text)));

    /// <summary>Instrução <c>WORD</c> com os bytes crus nos recursos (para os códigos de controle <c>EF xx</c>).</summary>
    public SyntheticYstbBuilder AddWordBytes(byte[] bytes, uint lineNumber) =>
        AddCommand("WORD", lineNumber, ResourceArgument(bytes));

    /// <summary>
    /// Argumento de expressão com uma string literal: <c>4D &lt;u16 tamanho&gt; aspas texto aspas</c>. O texto vai como está,
    /// com os escapes que o YU-RIS lê (<c>\\</c>, <c>\n</c>, <c>\t</c>), em Shift-JIS.
    /// </summary>
    public SyntheticYstbArgument StringExpression(string text, char quote = '"', ushort id = 0)
    {
        var body = GameEncodings.ShiftJis.GetBytes(quote + text + quote);
        var bytes = new byte[3 + body.Length];
        bytes[0] = 0x4D;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1), checked((ushort)body.Length));
        body.CopyTo(bytes, 3);
        return ResourceArgument(bytes, id);
    }

    /// <summary>Instrução <c>RETURNCODE</c>: o valor devolvido vai em <c>size</c> e o offset não aponta para recursos.</summary>
    public SyntheticYstbBuilder AddReturnCode(uint value, uint lineNumber, uint offset = 0) =>
        AddCommand("RETURNCODE", lineNumber, new SyntheticYstbArgument(0, 1, 0, value, offset));

    public SyntheticYstb Build()
    {
        var instructions = new byte[_instructions.Count * 4];
        var lineNumbers = new byte[_instructions.Count * 4];
        var arguments = new MemoryStream();
        for (var i = 0; i < _instructions.Count; i++)
        {
            var instruction = _instructions[i];
            instructions[i * 4] = instruction.Opcode;
            instructions[(i * 4) + 1] = checked((byte)instruction.Arguments.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(instructions.AsSpan((i * 4) + 2), instruction.Extra);
            BinaryPrimitives.WriteUInt32LittleEndian(lineNumbers.AsSpan(i * 4), instruction.LineNumber);
            foreach (var argument in instruction.Arguments)
            {
                WriteArgument(arguments, argument);
            }
        }

        var plainArguments = arguments.ToArray();
        var plainResources = _resources.ToArray();

        using var file = new MemoryStream();
        file.Write("YSTB"u8);
        WriteUInt32(file, _version);
        WriteUInt32(file, (uint)_instructions.Count);
        WriteUInt32(file, (uint)instructions.Length);
        WriteUInt32(file, (uint)plainArguments.Length);
        WriteUInt32(file, (uint)plainResources.Length);
        WriteUInt32(file, (uint)lineNumbers.Length);
        WriteUInt32(file, _padding);
        file.Write(Encrypt(instructions, _key));
        file.Write(Encrypt(plainArguments, _key));
        file.Write(Encrypt(plainResources, _key));
        file.Write(Encrypt(lineNumbers, _key));
        return new SyntheticYstb(file.ToArray(), _version, _key, _padding, _instructions.ToArray(), plainArguments, plainResources);
    }

    /// <summary>XOR cíclico com a chave em ordem big-endian, recomeçando no início da seção (simétrico).</summary>
    public static byte[] Encrypt(byte[] section, uint key)
    {
        Span<byte> keyBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyBytes, key);
        var result = new byte[section.Length];
        for (var i = 0; i < section.Length; i++)
        {
            result[i] = (byte)(section[i] ^ keyBytes[i % 4]);
        }

        return result;
    }

    private static void WriteArgument(Stream stream, SyntheticYstbArgument argument)
    {
        Span<byte> buffer = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, argument.Id);
        buffer[2] = argument.Type;
        buffer[3] = argument.AssignOp;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], argument.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], argument.Offset);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
