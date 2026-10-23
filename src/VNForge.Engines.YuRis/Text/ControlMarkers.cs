using System.Buffers;
using System.Globalization;
using System.Text;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Text;

/// <summary>
/// Converte os bytes de um <c>WORD</c> (Shift-JIS com códigos de controle <c>EF xx</c>) em texto com marcadores legíveis e de
/// volta. A gramática é a do doc 02 (seção "Unidade de texto"): <c>\n</c>, <c>\p</c>, <c>\c</c>, <c>\u</c>, <c>\\</c>, <c>\{</c>
/// e os marcadores genéricos que preservam os bytes.
/// </summary>
/// <remarks>
/// <para>
/// Marcadores genéricos: <c>{EF:xx}</c> é um código <c>EF xx</c> sem atalho (<c>xx</c> em hexadecimal); <c>{RAW:hhhh...}</c> guarda
/// bytes crus que não viram texto: um <c>EF</c> no fim dos bytes, um byte que o Shift-JIS não decodifica e um caractere que a
/// decodificação não devolve ao mesmo par de bytes (o CP932 tem pares duplicados, como <c>0x8790</c> e <c>0x81E0</c>, ambos
/// U+2252). Com isso <see cref="ToBytes"/> sempre reproduz os bytes de <see cref="ToMarkers"/>, o que a regra 3 do
/// <c>CLAUDE.md</c> exige.
/// </para>
/// <para>
/// A varredura anda pelos limites de caractere da página 932: um byte de abertura (0x81 a 0x9F, 0xE0 a 0xFC) leva o byte
/// seguinte junto, e é o que impede um <c>0xEF</c>, <c>0x5C</c> ou <c>0x7B</c> usado como segundo byte de virar controle,
/// barra ou chave.
/// </para>
/// </remarks>
public static class ControlMarkers
{
    private const byte ControlPrefix = 0xEF;
    private const byte LineBreakCode = 0xF0;
    private const byte PageCode = 0xF2;
    private const byte ClickCode = 0xF3;
    private const byte UnknownCode = 0xF5;

    private static readonly Encoding StrictShiftJis = CreateStrictShiftJis();

    private static Encoding CreateStrictShiftJis()
    {
        _ = GameEncodings.ShiftJis;
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>Converte os bytes do texto em texto com marcadores. Nunca falha: o que não vira texto vira <c>{RAW:...}</c>.</summary>
    public static string ToMarkers(ReadOnlySpan<byte> raw)
    {
        var sb = new StringBuilder(raw.Length);
        var i = 0;
        while (i < raw.Length)
        {
            var b = raw[i];
            if (b == ControlPrefix)
            {
                if (i + 1 < raw.Length)
                {
                    AppendControl(sb, raw[i + 1]);
                    i += 2;
                }
                else
                {
                    AppendRaw(sb, raw.Slice(i, 1));
                    i++;
                }

                continue;
            }

            var length = IsLeadByte(b) && i + 1 < raw.Length && IsTrailByte(raw[i + 1]) ? 2 : 1;
            var slice = raw.Slice(i, length);
            if (TryDecode(slice, out var ch))
            {
                AppendEscaped(sb, ch);
            }
            else
            {
                AppendRaw(sb, slice);
            }

            i += length;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converte texto com marcadores nos bytes do jogo. Os trechos de texto passam pelo <paramref name="encoder"/>; os bytes de
    /// controle são concatenados entre eles sem passar pelo codificador.
    /// </summary>
    /// <exception cref="FormatException">Marcador inválido; a mensagem cita a posição (em caracteres, a partir de 0).</exception>
    /// <exception cref="EncoderFallbackException">Um trecho de texto tem caractere que o codificador não representa.</exception>
    public static byte[] ToBytes(string text, ITextEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoder);

        var builder = new BytesBuilder(text.Length * 2, encoder);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                i = ReadEscape(text, i, builder);
            }
            else if (c == '{')
            {
                i = ReadBraced(text, i, builder);
            }
            else
            {
                builder.AppendText(c);
                i++;
            }
        }

        return builder.ToArray();
    }

    private static int ReadEscape(string text, int i, BytesBuilder builder)
    {
        if (i + 1 >= text.Length)
        {
            throw Invalid(i, "barra invertida no fim do texto; use \\\\ para uma barra literal");
        }

        var next = text[i + 1];
        if (next is '\\' or '{')
        {
            builder.AppendText(next);
        }
        else
        {
            var code = ControlCodeOf(next, i);
            builder.Flush();
            builder.AddBytes([ControlPrefix, code]);
        }

        return i + 2;
    }

    private static int ReadBraced(string text, int i, BytesBuilder builder)
    {
        var close = text.IndexOf('}', i + 1);
        if (close < 0)
        {
            throw Invalid(i, "chave sem fechamento; use \\{ para uma chave literal");
        }

        builder.Flush();
        builder.AddBytes(ParseGeneric(text.AsSpan(i + 1, close - i - 1), i));
        return close + 1;
    }

    /// <summary>Junta texto (que só passa pelo codificador ao fechar o trecho) e bytes de controle, na ordem.</summary>
    private sealed class BytesBuilder(int capacity, ITextEncoder encoder)
    {
        private readonly List<byte> _output = new(capacity);
        private readonly StringBuilder _pending = new();

        public void AppendText(char c) => _pending.Append(c);

        public void AddBytes(IEnumerable<byte> bytes) => _output.AddRange(bytes);

        public void Flush()
        {
            if (_pending.Length == 0)
            {
                return;
            }

            _output.AddRange(encoder.Encode(_pending.ToString()));
            _pending.Clear();
        }

        public byte[] ToArray()
        {
            Flush();
            return _output.ToArray();
        }
    }

    private static byte ControlCodeOf(char marker, int position) => marker switch
    {
        'n' => LineBreakCode,
        'p' => PageCode,
        'c' => ClickCode,
        'u' => UnknownCode,
        _ => throw Invalid(position, $"marcador \"\\{marker}\" desconhecido; use \\\\ para uma barra literal"),
    };

    private static void AppendControl(StringBuilder sb, byte code)
    {
        switch (code)
        {
            case LineBreakCode:
                sb.Append("\\n");
                break;
            case PageCode:
                sb.Append("\\p");
                break;
            case ClickCode:
                sb.Append("\\c");
                break;
            case UnknownCode:
                sb.Append("\\u");
                break;
            default:
                sb.Append("{EF:").Append(code.ToString("X2", CultureInfo.InvariantCulture)).Append('}');
                break;
        }
    }

    private static void AppendEscaped(StringBuilder sb, char ch)
    {
        switch (ch)
        {
            case '\\':
                sb.Append("\\\\");
                break;
            case '{':
                sb.Append("\\{");
                break;
            default:
                sb.Append(ch);
                break;
        }
    }

    private static void AppendRaw(StringBuilder sb, ReadOnlySpan<byte> bytes) =>
        sb.Append("{RAW:").Append(Convert.ToHexString(bytes)).Append('}');

    private static byte[] ParseGeneric(ReadOnlySpan<char> body, int position)
    {
        var colon = body.IndexOf(':');
        if (colon < 0)
        {
            throw Invalid(position, $"marcador \"{{{body}}}\" desconhecido; esperado {{EF:xx}} ou {{RAW:hex}}");
        }

        var name = body[..colon];
        var value = body[(colon + 1)..];
        if (name.SequenceEqual("EF"))
        {
            if (value.Length != 2 || !TryParseHex(value, out var code))
            {
                throw Invalid(position, $"marcador \"{{{body}}}\" inválido; {{EF:xx}} leva exatamente dois dígitos hexadecimais");
            }

            return [ControlPrefix, code[0]];
        }

        if (name.SequenceEqual("RAW"))
        {
            if (value.Length == 0 || value.Length % 2 != 0 || !TryParseHex(value, out var bytes))
            {
                throw Invalid(position, $"marcador \"{{{body}}}\" inválido; {{RAW:hex}} leva um número par de dígitos hexadecimais");
            }

            return bytes;
        }

        throw Invalid(position, $"marcador \"{{{body}}}\" desconhecido; esperado {{EF:xx}} ou {{RAW:hex}}");
    }

    private static bool TryParseHex(ReadOnlySpan<char> digits, out byte[] bytes)
    {
        bytes = new byte[digits.Length / 2];
        return Convert.FromHexString(digits, bytes, out _, out _) == OperationStatus.Done;
    }

    internal static bool IsLeadByte(byte b) => b is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC);

    internal static bool IsTrailByte(byte b) => b is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFC);

    private static bool TryDecode(ReadOnlySpan<byte> bytes, out char ch)
    {
        ch = '\0';
        Span<char> chars = stackalloc char[2];
        try
        {
            var count = StrictShiftJis.GetChars(bytes, chars);
            if (count != 1)
            {
                return false;
            }

            Span<byte> back = stackalloc byte[4];
            var backCount = StrictShiftJis.GetBytes(chars[..1], back);
            if (!back[..backCount].SequenceEqual(bytes))
            {
                return false;
            }

            ch = chars[0];
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static FormatException Invalid(int position, string reason) =>
        new($"Marcador de controle inválido na posição {position}: {reason}.");
}
