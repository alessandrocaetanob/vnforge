using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Text;

namespace VNForge.Core.Glossary;

/// <summary>
/// Envelope comum de <c>glossary.json</c> e <c>characters.json</c>: <c>version</c> e a lista <c>entries</c>, uma entrada por linha,
/// no mesmo estilo de <see cref="TextFileSerializer"/> (dois espaços, <c>\n</c>, japonês sem escape, fim com quebra de linha).
/// </summary>
internal static class EntryFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    public static string Serialize<T>(IReadOnlyList<T> entries, string filePath)
        where T : IGlossaryEntry
    {
        ArgumentNullException.ThrowIfNull(entries);
        CheckEntries(entries, filePath);

        var sb = new StringBuilder("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"entries\": [");
        if (entries.Count == 0)
        {
            return sb.Append("]\n}\n").ToString();
        }

        sb.Append('\n');
        for (var i = 0; i < entries.Count; i++)
        {
            string line;
            try
            {
                line = JsonSerializer.Serialize(entries[i], TextUnitJson.Options);
            }
            catch (JsonException ex)
            {
                throw new GlossaryFileException(filePath, $"a entrada '{entries[i].Source}' não pôde ser gravada: {ex.Message}", ex);
            }

            sb.Append("    ").Append(line).Append(i < entries.Count - 1 ? ",\n" : "\n");
        }

        return sb.Append("  ]\n}\n").ToString();
    }

    public static IReadOnlyList<T> Parse<T>(string json, string filePath)
        where T : class, IGlossaryEntry
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(filePath);

        CheckEnvelope(json, filePath);

        Envelope<T> envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<T>>(json, TextUnitJson.Options)
                ?? throw new GlossaryFileException(filePath, "o arquivo está vazio.");
        }
        catch (JsonException ex)
        {
            throw new GlossaryFileException(filePath, Describe(ex), ex);
        }

        var entries = new List<T>(envelope.Entries.Count);
        for (var i = 0; i < envelope.Entries.Count; i++)
        {
            entries.Add(envelope.Entries[i] ?? throw new GlossaryFileException(filePath, $"$.entries[{i}]: a entrada é nula."));
        }

        CheckEntries(entries, filePath);
        return entries;
    }

    private static void CheckEntries<T>(IReadOnlyList<T> entries, string filePath)
        where T : IGlossaryEntry
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            var source = entries[i].Source;
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new GlossaryFileException(filePath, $"$.entries[{i}].source: o texto original não pode ser vazio.");
            }

            if (!seen.Add(source))
            {
                throw new GlossaryFileException(filePath, $"'{source}' aparece mais de uma vez no arquivo.");
            }
        }
    }

    private static void CheckEnvelope(string json, string filePath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new GlossaryFileException(filePath, $"JSON malformado ({Position(ex)}).", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new GlossaryFileException(filePath, "a raiz deve ser um objeto JSON com 'version' e 'entries'.");
            }

            var hasVersion = false;
            var hasEntries = false;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "version":
                        hasVersion = true;
                        CheckVersion(property.Value, filePath);
                        break;
                    case "entries":
                        hasEntries = true;
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            throw new GlossaryFileException(filePath, "$.entries: deve ser uma lista.");
                        }

                        break;
                    default:
                        throw new GlossaryFileException(filePath, $"campo desconhecido '{property.Name}' na raiz do arquivo.");
                }
            }

            if (!hasVersion)
            {
                throw new GlossaryFileException(filePath, "campo obrigatório ausente: 'version'.");
            }

            if (!hasEntries)
            {
                throw new GlossaryFileException(filePath, "campo obrigatório ausente: 'entries'.");
            }
        }
    }

    private static void CheckVersion(JsonElement version, string filePath)
    {
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
        {
            throw new GlossaryFileException(filePath, $"$.version: deve ser o número inteiro {CurrentVersion}.");
        }

        if (number > CurrentVersion)
        {
            throw new GlossaryFileException(filePath, $"$.version: a versão {number} é mais nova que a {CurrentVersion}, a única que este VNForge lê; atualize o VNForge.");
        }

        if (number != CurrentVersion)
        {
            throw new GlossaryFileException(filePath, $"$.version: a versão {number} não existe; o valor esperado é {CurrentVersion}.");
        }
    }

    private static string Describe(JsonException ex)
    {
        var detail = ex.Message;
        var cut = detail.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut >= 0)
        {
            detail = detail[..cut];
        }

        var where = ex.Path is { Length: > 0 } path ? $"{path}, " : string.Empty;
        return $"{where}{Position(ex)}: {detail}";
    }

    private static string Position(JsonException ex) =>
        ex.LineNumber is { } line && ex.BytePositionInLine is { } column
            ? $"linha {line + 1}, coluna {column + 1}"
            : "posição desconhecida";

    private sealed record Envelope<T>(
        [property: JsonRequired] int Version,
        [property: JsonRequired] List<T?> Entries)
        where T : class;
}
