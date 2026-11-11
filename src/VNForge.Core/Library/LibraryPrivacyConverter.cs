using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// Lê e grava a <see cref="LibraryPrivacy"/> no <c>library.json</c>: <c>{ "showSpoilers": false, "adult": "blur" }</c>. O arquivo antigo tinha <c>showAdult</c> (verdadeiro ou falso)
/// no lugar de <c>adult</c>: verdadeiro vira <see cref="AdultMode.Show"/>, falso ou ausente vira <see cref="AdultMode.Blur"/>. Se os dois aparecem, <c>adult</c> vale.
/// </summary>
public sealed class LibraryPrivacyConverter : JsonConverter<LibraryPrivacy>
{
    public override LibraryPrivacy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A privacidade da biblioteca deveria ser um objeto.");
        }

        var showSpoilers = false;
        AdultMode? adult = null;
        var legacyShowAdult = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString() ?? string.Empty;
            reader.Read();
            if (name.Equals("showSpoilers", StringComparison.OrdinalIgnoreCase))
            {
                showSpoilers = reader.GetBoolean();
            }
            else if (name.Equals("adult", StringComparison.OrdinalIgnoreCase))
            {
                adult = ReadMode(ref reader);
            }
            else if (name.Equals("showAdult", StringComparison.OrdinalIgnoreCase))
            {
                legacyShowAdult = reader.GetBoolean();
            }
            else
            {
                reader.Skip();
            }
        }

        return new LibraryPrivacy(showSpoilers, adult ?? (legacyShowAdult ? AdultMode.Show : AdultMode.Blur));
    }

    public override void Write(Utf8JsonWriter writer, LibraryPrivacy value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(options.PropertyNamingPolicy?.ConvertName("ShowSpoilers") ?? "ShowSpoilers", value.ShowSpoilers);
        // O nome do modo vai como texto em qualquer serializador (o da CLI não tem o conversor de enum do arquivo).
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName("Adult") ?? "Adult", JsonNamingPolicy.CamelCase.ConvertName(value.Adult.ToString()));
        writer.WriteEndObject();
    }

    private static AdultMode ReadMode(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<AdultMode>(reader.GetString(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new JsonException("O modo do conteúdo adulto deveria ser show, blur ou hide.");
    }
}
