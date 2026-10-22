using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VNForge.Core.Text;

/// <summary>
/// Conteúdo de um arquivo <c>text/&lt;chave&gt;.json</c>: um envelope com <c>version</c> e a lista <c>units</c> das
/// <see cref="TextUnit"/> de um script, uma por linha, para o diff e o merge do git mostrarem só o que mudou.
/// </summary>
/// <remarks>
/// A gravação é estável: as unidades saem na ordem recebida, a indentação é de dois espaços, o fim de linha é <c>\n</c> e o texto termina
/// com uma quebra de linha; o <see cref="TextUnitJson"/> define cada unidade (japonês sem escape, marcadores como <c>\n</c> e <c>\p</c> intactos,
/// já que a barra invertida do marcador vira <c>\\</c> no JSON e volta igual). Gravar o mesmo conteúdo duas vezes gera o mesmo texto.
/// Quem grava no disco (<see cref="Projects.ProjectFolder"/>) usa UTF-8 sem BOM. Só aspas, barra invertida e U+0000 a U+001F são escapados
/// (<see cref="MinimalStringConverter"/>); o espaço ideográfico U+3000, o japonês e o emoji ficam literais.
/// </remarks>
public static partial class TextFileSerializer
{
    /// <summary>Versão do envelope que esta classe grava e lê.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>Serializa as unidades de um script, na ordem recebida.</summary>
    /// <param name="units">Unidades a gravar.</param>
    /// <param name="filePath">Caminho do arquivo de destino, só para as mensagens de erro.</param>
    /// <exception cref="TextFileException">Alguma unidade tem texto com UTF-16 inválido (surrogate solitário); a mensagem cita o id.</exception>
    public static string Serialize(IReadOnlyList<TextUnit> units, string filePath = "(memória)")
    {
        ArgumentNullException.ThrowIfNull(units);

        var sb = new StringBuilder("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"units\": [");
        if (units.Count == 0)
        {
            return sb.Append("]\n}\n").ToString();
        }

        sb.Append('\n');
        for (var i = 0; i < units.Count; i++)
        {
            sb.Append("    ").Append(SerializeUnit(units[i], filePath)).Append(i < units.Count - 1 ? ",\n" : "\n");
        }

        return sb.Append("  ]\n}\n").ToString();
    }

    /// <summary>Lê as unidades de um arquivo.</summary>
    /// <param name="json">Conteúdo do arquivo, sem BOM.</param>
    /// <param name="filePath">Caminho do arquivo, só para as mensagens de erro.</param>
    /// <param name="key">Chave do script dono do arquivo; quando informada, cada id precisa começar com <c>key + ":"</c> e não se repetir.</param>
    /// <exception cref="TextFileException">JSON malformado (com linha e coluna), envelope sem <c>version</c> ou <c>units</c>, versão desconhecida, campo
    /// desconhecido ou inválido (com o caminho do campo e a posição), unidade nula ou id fora da regra da <paramref name="key"/>.</exception>
    public static IReadOnlyList<TextUnit> Parse(string json, string filePath, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(filePath);

        var duplicate = CheckEnvelope(json, filePath);

        Envelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, TextUnitJson.Options)
                ?? throw new TextFileException(filePath, "o arquivo está vazio.");
        }
        catch (JsonException ex)
        {
            throw new TextFileException(filePath, DescribeInvalid(ex), ex);
        }

        if (duplicate is not null)
        {
            var name = DuplicateProperty().Match(duplicate.Message) is { Success: true } m ? $" '{m.Groups[1].Value}'" : string.Empty;
            throw new TextFileException(filePath, $"campo repetido{name} no arquivo.", duplicate);
        }

        var units = new List<TextUnit>(envelope.Units.Count);
        for (var i = 0; i < envelope.Units.Count; i++)
        {
            var unit = envelope.Units[i] ?? throw new TextFileException(filePath, $"$.units[{i}]: a unidade é nula.");
            try
            {
                units.Add(TextUnitJson.Validar(unit));
            }
            catch (JsonException ex)
            {
                throw new TextFileException(filePath, $"$.units[{i}]: {ex.Message}", ex);
            }
        }

        if (key is not null)
        {
            ValidateIds(units, key, filePath);
        }

        return units;
    }

    /// <summary>Confere que cada id começa com <c>key + ":"</c> e que nenhum se repete.</summary>
    /// <exception cref="TextFileException">Id de outro script ou repetido; a mensagem cita o id.</exception>
    public static void ValidateIds(IReadOnlyList<TextUnit> units, string key, string filePath)
    {
        ArgumentNullException.ThrowIfNull(units);
        var prefixo = key + ":";
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in units.Select(u => u.Id))
        {
            if (!id.StartsWith(prefixo, StringComparison.Ordinal))
            {
                throw new TextFileException(filePath, $"a unidade '{id}' não pertence ao script '{key}' (o id deveria começar com '{prefixo}').");
            }

            if (!vistos.Add(id))
            {
                throw new TextFileException(filePath, $"o id '{id}' aparece mais de uma vez no arquivo.");
            }
        }
    }

    private static string SerializeUnit(TextUnit unit, string filePath)
    {
        try
        {
            return TextUnitJson.Serialize(unit);
        }
        catch (JsonException ex)
        {
            throw new TextFileException(filePath, $"a unidade '{unit.Id}' não pôde ser gravada: {DetailSuffix().Replace(ex.Message, string.Empty)}", ex);
        }
    }

    /// <summary>Confere a raiz do arquivo. Devolve a exceção de campo repetido (sem posição no <c>JsonDocument</c>) para o chamador deixar o serializador apontar o campo; <c>null</c> se não há.</summary>
    private static JsonException? CheckEnvelope(string json, string filePath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException ex) when (ex.LineNumber is null || DuplicateProperty().IsMatch(ex.Message))
        {
            return ex;
        }
        catch (JsonException ex)
        {
            throw new TextFileException(filePath, $"JSON malformado ({Position(ex)}).", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new TextFileException(filePath, "a raiz deve ser um objeto JSON com 'version' e 'units'.");
            }

            var hasVersion = false;
            var hasUnits = false;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "version":
                        hasVersion = true;
                        CheckVersion(property.Value, filePath);
                        break;
                    case "units":
                        hasUnits = true;
                        if (property.Value.ValueKind != JsonValueKind.Array)
                        {
                            throw new TextFileException(filePath, "$.units: deve ser uma lista de unidades.");
                        }

                        break;
                    default:
                        throw new TextFileException(filePath, $"campo desconhecido '{property.Name}' na raiz do arquivo.");
                }
            }

            if (!hasVersion)
            {
                throw new TextFileException(filePath, "campo obrigatório ausente: 'version'.");
            }

            if (!hasUnits)
            {
                throw new TextFileException(filePath, "campo obrigatório ausente: 'units'.");
            }
        }

        return null;
    }

    private static void CheckVersion(JsonElement version, string filePath)
    {
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
        {
            throw new TextFileException(filePath, $"$.version: deve ser o número inteiro {CurrentVersion}.");
        }

        if (number > CurrentVersion)
        {
            throw new TextFileException(filePath, $"$.version: a versão {number} é mais nova que a {CurrentVersion}, a única que este VNForge lê; atualize o VNForge.");
        }

        if (number != CurrentVersion)
        {
            throw new TextFileException(filePath, $"$.version: a versão {number} não existe; o valor esperado é {CurrentVersion}.");
        }
    }

    private static string DescribeInvalid(JsonException ex)
    {
        var detail = DetailSuffix().Replace(ex.Message, string.Empty);
        var missing = MissingProperties().Match(detail);
        if (missing.Success)
        {
            detail = $"campo obrigatório ausente: {missing.Groups[1].Value}.";
        }
        else if (UnmappedProperty().Match(detail) is { Success: true } unmapped)
        {
            detail = $"campo desconhecido '{unmapped.Groups[1].Value}'.";
        }
        else if (DuplicateProperty().Match(detail) is { Success: true } duplicate)
        {
            detail = $"campo repetido '{duplicate.Groups[1].Value}'.";
        }
        else if (NullNotAllowed().IsMatch(detail))
        {
            detail = "o campo não aceita null.";
        }
        else if (NotConvertible().IsMatch(detail))
        {
            detail = "valor inválido ou de tipo errado para o campo.";
        }

        var where = ex.Path is { Length: > 0 } path ? $"{path}, " : string.Empty;
        return $"{where}{Position(ex)}: {detail}";
    }

    private static string Position(JsonException ex) =>
        ex.LineNumber is { } line && ex.BytePositionInLine is { } column
            ? $"linha {line + 1}, coluna {column + 1}"
            : "posição desconhecida";

    [GeneratedRegex(@"\s*Path:.*$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DetailSuffix();

    [GeneratedRegex(@"missing required properties[^:]*:\s*(.+?)\.?\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MissingProperties();

    [GeneratedRegex(@"The JSON property '([^']+)' could not be mapped", RegexOptions.CultureInvariant)]
    private static partial Regex UnmappedProperty();

    [GeneratedRegex(@"doesn't allow (?:setting |getting )?null value", RegexOptions.CultureInvariant)]
    private static partial Regex NullNotAllowed();

    [GeneratedRegex(@"[Dd]uplicate property '(.+?)'", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateProperty();

    [GeneratedRegex(@"could not be converted to", RegexOptions.CultureInvariant)]
    private static partial Regex NotConvertible();

    private sealed record Envelope(
        [property: JsonRequired] int Version,
        [property: JsonRequired] List<TextUnit?> Units);
}
