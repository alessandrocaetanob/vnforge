using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Converte <see cref="string"/> escapando só o que a gramática do JSON exige: aspas, barra invertida e U+0000 a U+001F.
/// O resto sai literal em UTF-8, inclusive o espaço ideográfico U+3000, o U+00A0, o U+2028 e emoji, que os codificadores do
/// .NET escapariam (<c>　</c> é ilegível no diff do <c>text/*.json</c>). Strings <c>null</c> não passam por aqui: o serializador grava <c>null</c>.
/// </summary>
internal sealed class MinimalStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw new JsonException("The JSON value could not be converted to System.String.");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteRawValue(Quote(value), skipInputValidation: false);

    internal static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[i + 1]);
                i += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw new JsonException($"o texto tem um caractere UTF-16 inválido (surrogate solitário U+{(int)c:X4}) na posição {i}.");
            }

            Append(sb, c);
            i++;
        }

        return sb.Append('"').ToString();
    }

    private static void Append(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            case '\b': sb.Append("\\b"); break;
            case '\f': sb.Append("\\f"); break;
            case < ' ': sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)); break;
            default: sb.Append(c); break;
        }
    }
}
