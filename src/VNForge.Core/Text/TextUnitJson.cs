using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Text;

/// <summary>
/// Formato JSON de <see cref="TextUnit"/> em <c>text/*.json</c>: nomes de propriedade em camelCase, na ordem do doc 02,
/// enums como string em minúsculas, texto Unicode sem escape (japonês legível em diff), indentado e com data em UTC.
/// </summary>
public static class TextUnitJson
{
    /// <summary>Opções compartilhadas. Não altere: o formato do arquivo depende delas.</summary>
    public static JsonSerializerOptions Options { get; } = CriarOpcoes();

    /// <summary>Serializa uma unidade.</summary>
    public static string Serialize(TextUnit unit) => JsonSerializer.Serialize(unit, Options);

    /// <summary>Lê uma unidade.</summary>
    /// <exception cref="JsonException">JSON inválido, campo obrigatório ausente ou valor de enum desconhecido.</exception>
    public static TextUnit Deserialize(string json) =>
        JsonSerializer.Deserialize<TextUnit>(json, Options) ?? throw new JsonException("A unidade de texto é nula.");

    /// <summary>Serializa as unidades de um script, na ordem recebida.</summary>
    public static string SerializeList(IReadOnlyList<TextUnit> units) => JsonSerializer.Serialize(units, Options);

    /// <summary>Lê as unidades de um script.</summary>
    /// <exception cref="JsonException">JSON inválido, campo obrigatório ausente ou valor de enum desconhecido.</exception>
    public static IReadOnlyList<TextUnit> DeserializeList(string json) =>
        JsonSerializer.Deserialize<List<TextUnit>>(json, Options) ?? throw new JsonException("A lista de unidades de texto é nula.");

    private static JsonSerializerOptions CriarOpcoes() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new UtcDateTimeOffsetConverter() },
    };

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        private const string Formato = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString(Formato, CultureInfo.InvariantCulture));
    }
}
