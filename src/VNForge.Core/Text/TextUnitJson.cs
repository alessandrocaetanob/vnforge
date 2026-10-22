using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VNForge.Core.Text;

/// <summary>
/// Formato JSON de <see cref="TextUnit"/> em <c>text/*.json</c>: nomes de propriedade em camelCase, na ordem do doc 02,
/// enums como string em minúsculas, texto Unicode sem escape (japonês legível em diff), um objeto compacto por linha
/// e data em UTC. Na leitura, campo desconhecido é erro (um erro de digitação como <c>traslation</c> não pode apagar a tradução
/// no próximo salvamento); comentários e vírgula final são aceitos, como no <c>vnforge.json</c>.
/// </summary>
/// <remarks>
/// Estas funções leem e gravam só as unidades. O arquivo <c>text/*.json</c> envolve a lista num envelope com <c>version</c>
/// (<see cref="TextFileSerializer"/>); o envelope não faz parte deste tipo.
/// </remarks>
public static partial class TextUnitJson
{
    /// <summary>Opções compartilhadas (somente leitura): o formato do arquivo depende delas.</summary>
    public static JsonSerializerOptions Options { get; } = CriarOpcoes();

    /// <summary>Serializa uma unidade, compacta e em uma linha.</summary>
    public static string Serialize(TextUnit unit) => JsonSerializer.Serialize(unit, Options);

    /// <summary>Lê uma unidade.</summary>
    /// <exception cref="JsonException">JSON inválido, campo obrigatório ausente ou nulo, campo desconhecido, <c>flags</c> com elemento nulo, <c>origin.at</c> sem fuso ou valor de enum desconhecido.</exception>
    public static TextUnit Deserialize(string json) =>
        Validar(JsonSerializer.Deserialize<TextUnit>(json, Options) ?? throw new JsonException("A unidade de texto é nula."));

    /// <summary>
    /// Serializa as unidades de um script, na ordem recebida: um array JSON com um objeto compacto por linha
    /// (<c>[</c>, as unidades separadas por vírgula e <c>]</c>), bom para diff e merge. Lista vazia vira <c>[]</c>.
    /// </summary>
    public static string SerializeList(IReadOnlyList<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0)
        {
            return "[]";
        }

        var sb = new StringBuilder("[\n");
        for (var i = 0; i < units.Count; i++)
        {
            sb.Append(Serialize(units[i])).Append(i < units.Count - 1 ? ",\n" : "\n");
        }

        return sb.Append(']').ToString();
    }

    /// <summary>Lê as unidades de um script.</summary>
    /// <exception cref="JsonException">As mesmas de <see cref="Deserialize"/>, ou elemento nulo na lista.</exception>
    public static IReadOnlyList<TextUnit> DeserializeList(string json)
    {
        var lista = JsonSerializer.Deserialize<List<TextUnit?>>(json, Options) ?? throw new JsonException("A lista de unidades de texto é nula.");
        return lista.Select(u => Validar(u ?? throw new JsonException("A lista de unidades tem um elemento nulo."))).ToList();
    }

    internal static TextUnit Validar(TextUnit unit)
    {
        if (unit.Flags.Any(f => f is null))
        {
            throw new JsonException($"A unidade '{unit.Id}' tem um elemento nulo em 'flags'.");
        }

        return unit;
    }

    private static JsonSerializerOptions CriarOpcoes()
    {
        var opcoes = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            AllowDuplicateProperties = false,
            Converters = { new UtcDateTimeOffsetConverter() },
        };
        opcoes.MakeReadOnly(populateMissingResolver: true);
        return opcoes;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex DataComFuso();

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        private const string Formato = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var texto = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (texto is null || !DataComFuso().IsMatch(texto)
                || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            {
                throw new JsonException("A data precisa estar em ISO 8601 com 'Z' ou deslocamento, como 2026-10-17T18:00:00Z.");
            }

            return data;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString(Formato, CultureInfo.InvariantCulture));
    }
}
