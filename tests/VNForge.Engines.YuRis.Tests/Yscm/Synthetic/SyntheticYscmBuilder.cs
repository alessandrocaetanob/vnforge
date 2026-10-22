using System.Buffers.Binary;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Tests.Yscm.Synthetic;

/// <summary>Parâmetro de um comando do YSCM sintético.</summary>
public sealed record SyntheticYscmArgument(string Name, byte TypeCode = 0, byte Flags = 0);

/// <summary>Comando do YSCM sintético; o id é a posição na lista.</summary>
public sealed record SyntheticYscmCommand(byte Id, string Name, IReadOnlyList<SyntheticYscmArgument> Arguments);

/// <summary>Resultado do <see cref="SyntheticYscmBuilder"/>: os bytes do <c>ysc.ybn</c> e o que foi escrito.</summary>
public sealed record SyntheticYscm(byte[] Bytes, uint Version, IReadOnlyList<SyntheticYscmCommand> Commands, int ErrorMessageCount)
{
    public byte IdOf(string name) => Commands.Single(c => c.Name == name).Id;
}

/// <summary>
/// Monta um <c>ysc.ybn</c> (YSCM) sintético: cabeçalho de 16 bytes, comandos, mensagens de erro e a tabela de
/// 256 bytes (docs/06-yuris-notas-tecnicas.md). Os nomes e a ordem dos comandos são escolhas dos testes, não os
/// de um jogo: o leitor não pode depender de posições fixas.
/// </summary>
public sealed class SyntheticYscmBuilder
{
    /// <summary>Quantidade de mensagens de erro que o yuri.md e o yscm.py gravam (<c>NErrStr</c>).</summary>
    public const int DefaultErrorMessageCount = 37;

    private readonly List<SyntheticYscmCommand> _commands = [];
    private uint _version = 500;
    private uint _padding;
    private int _errorMessageCount = DefaultErrorMessageCount;
    private int _tableSize = 256;

    public SyntheticYscmBuilder WithVersion(uint version)
    {
        _version = version;
        return this;
    }

    public SyntheticYscmBuilder WithPadding(uint padding)
    {
        _padding = padding;
        return this;
    }

    /// <summary>Quantidade de mensagens de erro depois dos comandos (37 no yuri, 0x90 nas notas do VNTextPatch).</summary>
    public SyntheticYscmBuilder WithErrorMessageCount(int count)
    {
        _errorMessageCount = count;
        return this;
    }

    /// <summary>Tamanho da tabela final (256 nas duas fontes).</summary>
    public SyntheticYscmBuilder WithTableSize(int size)
    {
        _tableSize = size;
        return this;
    }

    public SyntheticYscmBuilder AddCommand(string name, params string[] argumentNames) =>
        AddCommandWithArguments(name, argumentNames.Select(n => new SyntheticYscmArgument(n)).ToArray());

    public SyntheticYscmBuilder AddCommandWithArguments(string name, params SyntheticYscmArgument[] arguments)
    {
        _commands.Add(new SyntheticYscmCommand(checked((byte)_commands.Count), name, arguments));
        return this;
    }

    /// <summary>Acrescenta <paramref name="count"/> comandos <c>CMD000</c>, <c>CMD001</c> e assim por diante.</summary>
    public SyntheticYscmBuilder AddFillerCommands(int count)
    {
        var start = _commands.Count;
        for (var i = 0; i < count; i++)
        {
            AddCommand($"CMD{start + i:D3}");
        }

        return this;
    }

    /// <summary>Uma lista parecida com a de um jogo, com os cinco comandos de texto em posições arbitrárias.</summary>
    public SyntheticYscmBuilder WithTextCommands() => this
        .AddCommand("IF", "COND")
        .AddCommand("ELSE")
        .AddCommand("LOOP", "COUNT", "STEP")
        .AddCommand("_", "EXPR")
        .AddCommand("GOSUB", "LABEL", "PINT1", "PSTR2")
        .AddCommand("RETURN")
        .AddCommand("WORD", "TEXT")
        .AddCommand("RETURNCODE", "CODE")
        .AddCommand("WAIT", "TIME")
        .AddCommand("CG", "FILE", "X", "Y");

    public SyntheticYscm Build()
    {
        var sjis = GameEncodings.ShiftJis;
        using var stream = new MemoryStream();
        stream.Write("YSCM"u8);
        WriteUInt32(stream, _version);
        WriteUInt32(stream, (uint)_commands.Count);
        WriteUInt32(stream, _padding);

        foreach (var command in _commands)
        {
            WriteZeroTerminated(stream, sjis.GetBytes(command.Name));
            stream.WriteByte(checked((byte)command.Arguments.Count));
            foreach (var argument in command.Arguments)
            {
                WriteZeroTerminated(stream, sjis.GetBytes(argument.Name));
                stream.WriteByte(argument.TypeCode);
                stream.WriteByte(argument.Flags);
            }
        }

        for (var i = 0; i < _errorMessageCount; i++)
        {
            WriteZeroTerminated(stream, sjis.GetBytes($"エラー{i:D2}"));
        }

        stream.Write(new byte[_tableSize]);
        return new SyntheticYscm(stream.ToArray(), _version, _commands.ToArray(), _errorMessageCount);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteZeroTerminated(Stream stream, byte[] bytes)
    {
        stream.Write(bytes);
        stream.WriteByte(0);
    }
}
