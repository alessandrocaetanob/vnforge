using System.Globalization;
using VNForge.Engines.YuRis.Text;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// Converte um <see cref="YstbFile"/> em <see cref="YstbDump"/>. Só lê: nada é gravado e o script não muda. Os textos usam
/// <see cref="ControlMarkers"/> (<c>\n</c>, <c>\p</c>, <c>{EF:xx}</c>, <c>{RAW:hex}</c>) e as strings de expressão passam por
/// <c>ExpressionStrings</c>, como na extração de texto.
/// </summary>
public static class YstbDumper
{
    private static readonly string[] AssignOperators = ["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^="];

    /// <summary>Despeja <paramref name="script"/>, com os nomes de comando e de parâmetro de <paramref name="commands"/>.</summary>
    /// <param name="script">Script lido com o mesmo YSCM.</param>
    /// <param name="commands">O <c>ysc.ybn</c> do mesmo jogo.</param>
    public static YstbDump Dump(YstbFile script, YscmFile commands)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(commands);

        var rules = new Rules(commands);
        var instructions = new List<YstbDumpInstruction>(script.Instructions.Count);
        foreach (var instruction in script.Instructions)
        {
            var parameters = instruction.Opcode < commands.Commands.Count
                ? commands.Commands[instruction.Opcode].Arguments
                : [];
            var arguments = new List<YstbDumpArgument>(instruction.Arguments.Count);
            for (var i = 0; i < instruction.Arguments.Count; i++)
            {
                arguments.Add(DumpArgument(script, instruction, i, parameters, rules));
            }

            instructions.Add(new YstbDumpInstruction(
                instruction.Index, instruction.LineNumber, instruction.Opcode, instruction.CommandName, instruction.Extra, arguments));
        }

        return new YstbDump(script.Source, script.Version, script.Key, script.KeyOrigin, script.ArgumentCount, instructions);
    }

    private static YstbDumpArgument DumpArgument(
        YstbFile script, YstbInstruction instruction, int index, IReadOnlyList<YscmArgument> parameters, Rules rules)
    {
        var argument = instruction.Arguments[index];
        var (kind, value) = Describe(script, instruction, index, rules);
        var name = argument.Id < parameters.Count ? parameters[argument.Id].Name : null;
        var op = argument.AssignOp < AssignOperators.Length
            ? AssignOperators[argument.AssignOp]
            : argument.AssignOp.ToString(CultureInfo.InvariantCulture);
        return new YstbDumpArgument(index, argument.Id, name, argument.Type, argument.AssignOp, op, argument.Size, argument.Offset, kind, value);
    }

    private static (YstbArgumentKind Kind, string? Value) Describe(YstbFile script, YstbInstruction instruction, int index, Rules rules)
    {
        var argument = instruction.Arguments[index];
        if (rules.IsExempt(instruction.Opcode, index, instruction.Arguments.Count))
        {
            return (YstbArgumentKind.Value, null);
        }

        if (argument.Size == 0)
        {
            return (YstbArgumentKind.Empty, null);
        }

        var bytes = script.GetResource(argument).Span;
        if (instruction.Opcode == rules.Word)
        {
            return (YstbArgumentKind.Text, ControlMarkers.ToMarkers(bytes));
        }

        return ExpressionStrings.Read(bytes, out var text) == ExpressionStringStatus.Literal
            ? (YstbArgumentKind.String, ControlMarkers.ToMarkers(text))
            : (YstbArgumentKind.Expression, Convert.ToHexString(bytes));
    }

    /// <summary>Os opcodes que mudam a leitura dos argumentos, com as mesmas exceções do <see cref="YstbReader"/>.</summary>
    private readonly struct Rules(YscmFile commands)
    {
        private readonly YuRisOpcodes _opcodes = commands.GetOpcodes();
        private readonly byte? _if = Find(commands, "IF");
        private readonly byte? _else = Find(commands, "ELSE");
        private readonly byte? _loop = Find(commands, "LOOP");

        public byte Word => _opcodes.Word;

        public bool IsExempt(byte opcode, int argumentIndex, int declared) =>
            opcode == _opcodes.ReturnCode
            || ((opcode == _if || opcode == _else) && declared == 3 && argumentIndex >= 1)
            || (opcode == _loop && argumentIndex == 1);

        private static byte? Find(YscmFile commands, string name) => commands.TryGetOpcode(name, out var opcode) ? opcode : null;
    }
}
