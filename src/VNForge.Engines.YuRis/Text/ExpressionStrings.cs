using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Text;

/// <summary>Resultado de <see cref="ExpressionStrings.Read"/>.</summary>
internal enum ExpressionStringStatus
{
    /// <summary>A expressão não é só uma string literal (outro opcode, concatenação, aspas diferentes, tamanho que não fecha).</summary>
    NotLiteral,

    /// <summary>É uma string literal, mas tem um escape que o YU-RIS não define; não dá para converter sem mudar o sentido.</summary>
    InvalidEscape,

    /// <summary>String literal lida.</summary>
    Literal,
}

/// <summary>
/// Lê a string literal de uma expressão do YU-RIS: o opcode <c>4D</c>, o tamanho em 16 bits e o texto entre aspas
/// (docs/06-yuris-notas-tecnicas.md). Só vale a expressão que é exatamente isso; concatenações e variáveis ficam de fora.
/// </summary>
internal static class ExpressionStrings
{
    private const byte PushStringOpcode = 0x4D;
    private const byte Backslash = 0x5C;
    private const byte ControlPrefix = 0xEF;
    private const byte LineBreakCode = 0xF0;

    /// <summary>
    /// Devolve o texto da string em bytes do jogo, sem as aspas e com os escapes de C desfeitos: <c>\\</c> vira uma barra,
    /// <c>\n</c> vira o código de quebra de linha <c>EF F0</c> (o marcador <c>\n</c>) e <c>\t</c> vira o byte 09. O
    /// <c>ControlMarkers.ToMarkers</c> converte o resultado em texto com marcadores. As aspas valem se a primeira e a última
    /// forem iguais e forem <c>"</c>, <c>'</c> ou crase; é só o que o runtime confere e o que o doc 06 descreve.
    /// </summary>
    public static ExpressionStringStatus Read(ReadOnlySpan<byte> expression, out byte[] text)
    {
        text = [];
        if (expression.Length < 5 || expression[0] != PushStringOpcode)
        {
            return ExpressionStringStatus.NotLiteral;
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(expression[1..]);
        if (length < 2 || expression.Length != 3 + length)
        {
            return ExpressionStringStatus.NotLiteral;
        }

        var body = expression[3..];
        var quote = body[0];
        if (quote is not ((byte)'"' or (byte)'\'' or (byte)'`') || body[^1] != quote)
        {
            return ExpressionStringStatus.NotLiteral;
        }

        return Unescape(body[1..^1], out text) ? ExpressionStringStatus.Literal : ExpressionStringStatus.InvalidEscape;
    }

    private static bool Unescape(ReadOnlySpan<byte> inner, out byte[] text)
    {
        var output = new List<byte>(inner.Length);
        var i = 0;
        while (i < inner.Length)
        {
            var b = inner[i];
            if (b == Backslash)
            {
                if (i + 1 >= inner.Length)
                {
                    text = [];
                    return false;
                }

                switch (inner[i + 1])
                {
                    case Backslash:
                        output.Add(Backslash);
                        break;
                    case (byte)'n':
                        output.Add(ControlPrefix);
                        output.Add(LineBreakCode);
                        break;
                    case (byte)'t':
                        output.Add(0x09);
                        break;
                    default:
                        text = [];
                        return false;
                }

                i += 2;
                continue;
            }

            if (ControlMarkers.IsLeadByte(b) && i + 1 < inner.Length && ControlMarkers.IsTrailByte(inner[i + 1]))
            {
                output.Add(b);
                output.Add(inner[i + 1]);
                i += 2;
                continue;
            }

            output.Add(b);
            i++;
        }

        text = output.ToArray();
        return true;
    }
}
