using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using VNForge.Core.Engines;
using VNForge.Core.Text;
using VNForge.Engines.YuRis.Ystb;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// Põe as traduções de volta num script YSTB já lido: faz o caminho inverso do <see cref="YstbTextExtractor"/> e entrega ao
/// <see cref="YstbWriter"/> só os argumentos que mudam. As unidades são casadas com o script pelo <see cref="TextUnit.Locator"/>;
/// o <see cref="TextUnit.Id"/> só serve à validação e às mensagens de erro.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mensagem (<c>WORD</c>).</b> A tradução inteira, já com o falante e as aspas japonesas de volta quando o original tinha
/// (<c>name + open + tradução + close</c>), vai no primeiro <c>WORD</c> da mensagem; os outros ficam com tamanho 0.
/// </para>
/// <para>
/// <b>Strings de expressão</b> (<c>GOSUB</c> e as unidades <c>other</c>). O texto volta a <c>4D &lt;u16 tamanho&gt; aspas texto aspas</c>,
/// com a barra, a quebra de linha (<c>EF F0</c>) e a tabulação refeitas como <c>\\</c>, <c>\n</c> e <c>\t</c>. O delimitador é o do
/// original; se o texto novo tem esse caractere, vale o primeiro de <c>"</c>, crase e <c>'</c> que o texto não tem (o YU-RIS não tem
/// escape de aspas, mas só confere a primeira e a última). Texto com os três é recusado.
/// </para>
/// <para>
/// <b>Falante.</b> Vale o <see cref="TextUnit.Speaker"/> da unidade quando preenchido; vazio, o nome do locator.
/// </para>
/// </remarks>
public static class YstbTextInjector
{
    private const int MaxExpressionLength = ushort.MaxValue;
    private const byte Backslash = 0x5C;
    private const byte ControlPrefix = 0xEF;
    private const byte LineBreakCode = 0xF0;
    private const byte Tab = 0x09;
    private const string WordCommand = "WORD";

    private static readonly byte[] Delimiters = [(byte)'"', (byte)'`', (byte)'\''];

    /// <summary>Gera o script com as traduções aplicadas; o que não muda sai byte a byte igual.</summary>
    /// <param name="file">O script original, para a chave dos ids.</param>
    /// <param name="script">O mesmo script, lido por <see cref="YstbReader"/>.</param>
    /// <param name="units">Unidades com as traduções, de qualquer ordem e em qualquer quantidade; as que não precisam de reescrita (<see cref="TextUnit.NeedsRewrite"/> falso) são ignoradas.</param>
    /// <param name="encoder">Codificador do texto para os bytes do jogo.</param>
    /// <exception cref="ArgumentException">Id de outro script ou repetido, ou tradução que não cabe numa string de expressão.</exception>
    /// <exception cref="InvalidDataException">O locator de uma unidade a reescrever falta, é de outra versão ou não corresponde ao script ou ao <see cref="TextUnit.Source"/>.</exception>
    /// <exception cref="EncoderFallbackException">Uma tradução tem caractere que o <paramref name="encoder"/> não representa; a mensagem cita o id.</exception>
    /// <exception cref="FormatException">Uma tradução tem marcador inválido; a mensagem cita o id.</exception>
    public static byte[] Inject(ScriptFile file, YstbFile script, IReadOnlyList<TextUnit> units, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(encoder);

        file.ValidateUnits(units);

        var replacements = new Dictionary<int, ReadOnlyMemory<byte>>();
        foreach (var unit in units)
        {
            if (unit.NeedsRewrite)
            {
                Plan(script, unit, encoder, replacements);
            }
        }

        return YstbWriter.Write(script, replacements);
    }

    private static void Plan(YstbFile script, TextUnit unit, ITextEncoder encoder, Dictionary<int, ReadOnlyMemory<byte>> replacements)
    {
        var locator = Locator.Parse(unit);
        var targets = locator.Resolve(script, unit);
        var original = locator.Command == WordCommand
            ? ReadWordOriginal(script, locator, targets, unit)
            : ReadExpressionOriginal(script, targets, unit);

        var text = locator.Speaker is { } speaker
            ? (string.IsNullOrEmpty(unit.Speaker) ? speaker.Name : unit.Speaker) + speaker.Open + unit.Translation + speaker.Close
            : unit.Translation!;
        var encoded = Encode(unit, text, encoder);

        var rewritten = locator.Command == WordCommand ? encoded : BuildExpression(unit, encoded, original);
        if (rewritten.AsSpan().SequenceEqual(original))
        {
            return;
        }

        for (var i = 0; i < targets.Count; i++)
        {
            if (!replacements.TryAdd(targets[i].ArgumentIndex, i == 0 ? rewritten : ReadOnlyMemory<byte>.Empty))
            {
                throw Fail(unit, $"o argumento {Number(targets[i].Argument)} da instrução {Number(targets[i].Instruction)} também é de outra unidade.");
            }
        }
    }

    private static byte[] Encode(TextUnit unit, string text, ITextEncoder encoder)
    {
        try
        {
            return ControlMarkers.ToBytes(text, encoder);
        }
        catch (EncoderFallbackException e)
        {
            throw new EncoderFallbackException($"A tradução da unidade '{unit.Id}' não pode ser gravada: {e.Message}", e);
        }
        catch (FormatException e)
        {
            throw new FormatException($"A tradução da unidade '{unit.Id}' tem um marcador inválido: {e.Message}", e);
        }
    }

    /// <summary>Confere que as partes ainda decodificam para o <see cref="TextUnit.Source"/> e devolve os bytes juntos.</summary>
    private static byte[] ReadWordOriginal(YstbFile script, Locator locator, IReadOnlyList<Target> targets, TextUnit unit)
    {
        var markers = new StringBuilder();
        var bytes = new List<byte>();
        foreach (var target in targets)
        {
            var raw = script.GetResource(target.ArgumentValue).Span;
            markers.Append(ControlMarkers.ToMarkers(raw));
            bytes.AddRange(raw);
        }

        var expected = locator.Speaker is { } speaker ? speaker.Name + speaker.Open + unit.Source + speaker.Close : unit.Source;
        if (!string.Equals(markers.ToString(), expected, StringComparison.Ordinal))
        {
            throw Fail(unit, "o texto do script não é mais o do original (o script é de outra versão ou o locator está desatualizado).");
        }

        return [.. bytes];
    }

    private static byte[] ReadExpressionOriginal(YstbFile script, IReadOnlyList<Target> targets, TextUnit unit)
    {
        if (targets.Count != 1)
        {
            throw Fail(unit, $"uma string de expressão tem uma parte só, mas o locator lista {Number(targets.Count)}.");
        }

        var raw = script.GetResource(targets[0].ArgumentValue).Span;
        if (ExpressionStrings.Read(raw, out var text) != ExpressionStringStatus.Literal
            || !string.Equals(ControlMarkers.ToMarkers(text), unit.Source, StringComparison.Ordinal))
        {
            throw Fail(unit, "o trecho do script não é mais a string do original (o script é de outra versão ou o locator está desatualizado).");
        }

        return raw.ToArray();
    }

    /// <summary>Refaz <c>4D &lt;u16 tamanho&gt; aspas texto aspas</c> a partir dos bytes do jogo já codificados.</summary>
    private static byte[] BuildExpression(TextUnit unit, byte[] content, byte[] original)
    {
        var originalQuote = original[3];
        var inner = new List<byte>(content.Length + 8);
        var present = new HashSet<byte>();
        var i = 0;
        while (i < content.Length)
        {
            var b = content[i];
            if (b == ControlPrefix && i + 1 < content.Length && content[i + 1] == LineBreakCode)
            {
                inner.AddRange("\\n"u8);
                i += 2;
            }
            else if (ControlMarkers.IsLeadByte(b) && i + 1 < content.Length && ControlMarkers.IsTrailByte(content[i + 1]))
            {
                inner.Add(b);
                inner.Add(content[i + 1]);
                i += 2;
            }
            else
            {
                present.Add(b);
                if (b == Backslash)
                {
                    inner.AddRange("\\\\"u8);
                }
                else if (b == Tab)
                {
                    inner.AddRange("\\t"u8);
                }
                else
                {
                    inner.Add(b);
                }

                i++;
            }
        }

        if (original.Length == inner.Count + 5 && original.AsSpan(4, inner.Count).SequenceEqual(CollectionsMarshal.AsSpan(inner)))
        {
            return original;
        }

        var quote = ChooseQuote(originalQuote, present);
        if (quote is null)
        {
            throw new ArgumentException(
                $"A tradução da unidade '{unit.Id}' tem aspas duplas, crase e apóstrofo ao mesmo tempo, e o YU-RIS não tem escape de aspas para delimitar a string; tire um dos três.",
                nameof(unit));
        }

        var length = inner.Count + 2;
        if (length > MaxExpressionLength)
        {
            throw new ArgumentException(
                $"A tradução da unidade '{unit.Id}' ocupa {Number(length)} bytes na string de expressão, acima do limite de {Number(MaxExpressionLength)}.",
                nameof(unit));
        }

        var result = new byte[3 + length];
        result[0] = 0x4D;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(1), (ushort)length);
        result[3] = quote.Value;
        inner.CopyTo(result, 4);
        result[^1] = quote.Value;
        return result;
    }

    private static byte? ChooseQuote(byte original, HashSet<byte> present)
    {
        if (!present.Contains(original))
        {
            return original;
        }

        foreach (var candidate in Delimiters)
        {
            if (!present.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static InvalidDataException Fail(TextUnit unit, string reason) =>
        new($"A unidade '{unit.Id}' não pode ser reescrita: {reason}");

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private readonly record struct Speaker(string Name, string Open, string Close);

    private readonly record struct Target(int Instruction, int Argument, int ArgumentIndex, YstbArgument ArgumentValue);

    private sealed record Locator(string Command, List<(int Instruction, int Argument, uint Size, uint Offset)> Parts, Speaker? Speaker)
    {
        public static Locator Parse(TextUnit unit)
        {
            if (unit.Locator is not { ValueKind: JsonValueKind.Object } root)
            {
                throw Fail(unit, "ela não tem locator.");
            }

            if (!TryGetNumber(root, "version", out var number) || number != YstbTextExtractor.LocatorVersion)
            {
                throw Fail(unit, $"o locator não é da versão {Number(YstbTextExtractor.LocatorVersion)}.");
            }

            if (!root.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("parts", out var partsElement) || partsElement.ValueKind != JsonValueKind.Array
                || partsElement.GetArrayLength() == 0)
            {
                throw Fail(unit, "o locator não tem command e parts.");
            }

            var parts = new List<(int, int, uint, uint)>();
            foreach (var part in partsElement.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object
                    || !TryGetNumber(part, "instruction", out var instructionIndex) || instructionIndex is < 0 or > int.MaxValue
                    || !TryGetNumber(part, "argument", out var argumentIndex) || argumentIndex is < 0 or > int.MaxValue
                    || !TryGetNumber(part, "size", out var sizeValue) || sizeValue is < 0 or > uint.MaxValue
                    || !TryGetNumber(part, "offset", out var offsetValue) || offsetValue is < 0 or > uint.MaxValue)
                {
                    throw Fail(unit, "uma parte do locator não tem instruction, argument, size e offset numéricos dentro dos limites.");
                }

                parts.Add(((int)instructionIndex, (int)argumentIndex, (uint)sizeValue, (uint)offsetValue));
            }

            Speaker? speaker = null;
            if (root.TryGetProperty("speaker", out var speakerElement))
            {
                if (speakerElement.ValueKind != JsonValueKind.Object
                    || !TryGetString(speakerElement, "name", out var name)
                    || !TryGetString(speakerElement, "open", out var open)
                    || !TryGetString(speakerElement, "close", out var close))
                {
                    throw Fail(unit, "o falante do locator não tem name, open e close.");
                }

                speaker = new Speaker(name, open, close);
            }

            return new Locator(command.GetString()!, parts, speaker);
        }

        /// <summary>Acha cada parte no script e confere que o argumento ainda tem o <c>size</c> e o <c>offset</c> da extração.</summary>
        public List<Target> Resolve(YstbFile script, TextUnit unit)
        {
            var targets = new List<Target>(Parts.Count);
            foreach (var (instructionIndex, argumentIndex, size, offset) in Parts)
            {
                if (instructionIndex < 0 || instructionIndex >= script.Instructions.Count)
                {
                    throw Fail(unit, $"o locator aponta para a instrução {Number(instructionIndex)}, e o script tem {Number(script.Instructions.Count)}.");
                }

                var instruction = script.Instructions[instructionIndex];
                if (!string.Equals(instruction.CommandName, Command, StringComparison.Ordinal))
                {
                    throw Fail(unit, $"a instrução {Number(instructionIndex)} é {instruction.CommandName}, e o locator diz {Command}.");
                }

                if (argumentIndex < 0 || argumentIndex >= instruction.Arguments.Count)
                {
                    throw Fail(unit, $"a instrução {Number(instructionIndex)} não tem o argumento {Number(argumentIndex)}.");
                }

                var argument = instruction.Arguments[argumentIndex];
                if (argument.Size != size || argument.Offset != offset)
                {
                    throw Fail(unit, $"o argumento {Number(argumentIndex)} da instrução {Number(instructionIndex)} não está mais em size {Number(size)} e offset {Number(offset)} (o script é de outra versão).");
                }

                targets.Add(new Target(instructionIndex, argumentIndex, instruction.FirstArgumentIndex + argumentIndex, argument));
            }

            return targets;
        }

        private static bool TryGetNumber(JsonElement element, string name, out long value)
        {
            value = 0;
            return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
        }

        private static bool TryGetString(JsonElement element, string name, out string value)
        {
            value = string.Empty;
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString()!;
            return true;
        }
    }
}
