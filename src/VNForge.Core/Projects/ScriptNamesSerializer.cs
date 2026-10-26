using System.Text;
using System.Text.Json;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>
/// Lê e grava <c>scripts.json</c>: <c>{ "version": 1, "entries": [...] }</c> com um <see cref="ScriptNameEntry"/> por linha (<c>key</c>, <c>detected</c>, <c>name</c>,
/// <c>folder</c> e <c>group</c>, os quatro últimos opcionais), ordenadas pela chave, no estilo dos outros arquivos do projeto.
/// </summary>
public static class ScriptNamesSerializer
{
    private const int CurrentVersion = 1;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    public static string Serialize(ScriptNames names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var sb = new StringBuilder("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"entries\": [");
        if (names.Entries.Count == 0)
        {
            return sb.Append("]\n}\n").ToString();
        }

        sb.Append('\n');
        for (var i = 0; i < names.Entries.Count; i++)
        {
            var entry = names.Entries[i];
            sb.Append("    { \"key\": ").Append(Quote(entry.Key));
            if (entry.Detected is not null)
            {
                sb.Append(", \"detected\": ").Append(Quote(entry.Detected));
            }

            if (entry.Name is not null)
            {
                sb.Append(", \"name\": ").Append(Quote(entry.Name));
            }

            if (entry.Folder is not null)
            {
                sb.Append(", \"folder\": ").Append(Quote(entry.Folder));
            }

            if (entry.Group is not null)
            {
                sb.Append(", \"group\": ").Append(Quote(entry.Group));
            }

            sb.Append(" }").Append(i < names.Entries.Count - 1 ? ",\n" : "\n");
        }

        return sb.Append("  ]\n}\n").ToString();
    }

    /// <exception cref="GlossaryFileException">JSON malformado, campo desconhecido ou ausente, versão desconhecida, chave inválida ou repetida, entrada sem nome.</exception>
    public static ScriptNames Parse(string json, string filePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(filePath);

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
            return ReadRoot(document.RootElement, filePath);
        }
    }

    private static ScriptNames ReadRoot(JsonElement root, string filePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new GlossaryFileException(filePath, "a raiz deve ser um objeto JSON com 'version' e 'entries'.");
        }

        var hasVersion = false;
        JsonElement? entries = null;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version":
                    hasVersion = true;
                    CheckVersion(property.Value, filePath);
                    break;
                case "entries":
                    entries = property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value
                        : throw new GlossaryFileException(filePath, "$.entries: deve ser uma lista.");
                    break;
                default:
                    throw new GlossaryFileException(filePath, $"campo desconhecido '{property.Name}' na raiz do arquivo.");
            }
        }

        if (!hasVersion)
        {
            throw new GlossaryFileException(filePath, "campo obrigatório ausente: 'version'.");
        }

        if (entries is not { } list)
        {
            throw new GlossaryFileException(filePath, "campo obrigatório ausente: 'entries'.");
        }

        var result = new List<ScriptNameEntry>();
        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            result.Add(ReadEntry(item, index++, filePath));
        }

        var repeated = result.GroupBy(e => e.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
        {
            throw new GlossaryFileException(filePath, $"'{repeated.Key}' aparece mais de uma vez no arquivo.");
        }

        return ScriptNames.From(result);
    }

    private static ScriptNameEntry ReadEntry(JsonElement item, int index, string filePath)
    {
        var where = $"$.entries[{index}]";
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new GlossaryFileException(filePath, $"{where}: a entrada deve ser um objeto.");
        }

        string? key = null;
        string? detected = null;
        string? name = null;
        string? folder = null;
        string? group = null;
        foreach (var property in item.EnumerateObject())
        {
            switch (property.Name)
            {
                case "key":
                    key = ReadString(property.Value, $"{where}.key", filePath);
                    break;
                case "detected":
                    detected = ReadString(property.Value, $"{where}.detected", filePath);
                    break;
                case "name":
                    name = ReadString(property.Value, $"{where}.name", filePath);
                    break;
                case "folder":
                    folder = ReadString(property.Value, $"{where}.folder", filePath);
                    break;
                case "group":
                    group = ReadString(property.Value, $"{where}.group", filePath);
                    break;
                default:
                    throw new GlossaryFileException(filePath, $"{where}: campo desconhecido '{property.Name}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new GlossaryFileException(filePath, $"{where}.key: a chave do script é obrigatória e não pode ser vazia.");
        }

        if (key != key.Trim().ToLowerInvariant())
        {
            throw new GlossaryFileException(filePath, $"{where}.key: a chave '{key}' deve estar em minúsculas e sem espaços nas pontas.");
        }

        var entry = new ScriptNameEntry(key, Blank(detected), Blank(name)) { Folder = Blank(folder), Group = Blank(group) };
        if (entry.IsEmpty)
        {
            throw new GlossaryFileException(filePath, $"{where}: o script '{key}' está sem nome ('detected', 'name', 'folder' ou 'group').");
        }

        return entry;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string ReadString(JsonElement value, string path, string filePath) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new GlossaryFileException(filePath, $"{path}: deve ser um texto.");

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

    private static string Quote(string text) => JsonSerializer.Serialize(text, TextUnitJson.Options);

    private static string Position(JsonException ex) =>
        ex.LineNumber is { } line && ex.BytePositionInLine is { } column
            ? $"linha {line + 1}, coluna {column + 1}"
            : "posição desconhecida";
}
