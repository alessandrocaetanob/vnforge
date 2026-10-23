using System.Text.Encodings.Web;
using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Formatting;

/// <summary>Lê e grava o <c>formatting.json</c>. Campo ausente usa o padrão de <see cref="FormattingProfile.Default"/>; campo desconhecido é erro.</summary>
public static class FormattingProfileSerializer
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <exception cref="FormattingProfileException">JSON malformado, campo desconhecido ou valor fora do permitido.</exception>
    public static FormattingProfile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            var onde = ex.LineNumber is { } linha && ex.BytePositionInLine is { } coluna
                ? $"linha {linha + 1}, coluna {coluna + 1}"
                : "posição desconhecida";
            throw new FormattingProfileException("$", $"JSON malformado ({onde}).");
        }

        using (document)
        {
            return ReadProfile(document.RootElement);
        }
    }

    public static string Serialize(FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            writer.WriteStartObject();

            writer.WriteStartArray("kinds");
            foreach (var kind in profile.Kinds)
            {
                writer.WriteStringValue(KindName(kind));
            }

            writer.WriteEndArray();

            writer.WriteStartObject("wrap");
            writer.WriteBoolean("enabled", profile.Wrap.Enabled);
            writer.WriteNumber("maxWidth", profile.Wrap.MaxWidth);
            if (profile.Wrap.MaxLines is { } maxLines)
            {
                writer.WriteNumber("maxLines", maxLines);
            }
            else
            {
                writer.WriteNull("maxLines");
            }

            writer.WriteString("charWidth", CharWidthName(profile.Wrap.CharWidth));
            writer.WriteEndObject();

            if (!profile.Replacements.IsEmpty)
            {
                writer.WriteStartObject("replacements");
                WriteReplacements(writer, "before", profile.Replacements.Before);
                WriteReplacements(writer, "after", profile.Replacements.After);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteReplacements(Utf8JsonWriter writer, string name, IReadOnlyList<Replacement> replacements)
    {
        writer.WriteStartArray(name);
        foreach (var replacement in replacements)
        {
            writer.WriteStartObject();
            writer.WriteString("pattern", replacement.Pattern);
            writer.WriteString("with", replacement.With);
            if (replacement.Regex)
            {
                writer.WriteBoolean("regex", true);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ValidateReplacements(string name, IReadOnlyList<Replacement> replacements)
    {
        for (var i = 0; i < replacements.Count; i++)
        {
            if (ReplacementStage.Validate(replacements[i]) is { } error)
            {
                throw new FormattingProfileException($"$.replacements.{name}[{i}].pattern", error);
            }
        }
    }

    private static void Validate(FormattingProfile profile)
    {
        ValidateReplacements("before", profile.Replacements.Before);
        ValidateReplacements("after", profile.Replacements.After);

        if (profile.Kinds.Count == 0)
        {
            throw new FormattingProfileException("$.kinds", "informe ao menos um papel de linha.");
        }

        if (profile.Wrap.MaxWidth < 1)
        {
            throw new FormattingProfileException("$.wrap.maxWidth", "deve ser de ao menos 1 coluna.");
        }

        if (profile.Wrap.MaxLines is < 1)
        {
            throw new FormattingProfileException("$.wrap.maxLines", "deve ser de ao menos 1 linha, ou null para não limitar.");
        }
    }

    private static FormattingProfile ReadProfile(JsonElement root)
    {
        var reader = new ObjectReader(root, "$");
        var defaults = FormattingProfile.Default;

        var kinds = reader.OptionalKinds("kinds") ?? defaults.Kinds;

        var wrap = defaults.Wrap;
        if (reader.OptionalObject("wrap") is { } wrapReader)
        {
            wrap = new WrapRule(
                wrapReader.OptionalBool("enabled") ?? wrap.Enabled,
                wrapReader.OptionalInt("maxWidth") ?? wrap.MaxWidth,
                wrapReader.HasProperty("maxLines") ? wrapReader.OptionalInt("maxLines", allowNull: true) :wrap.MaxLines,
                wrapReader.OptionalCharWidth("charWidth") ?? wrap.CharWidth);
            if (wrap.MaxWidth < 1)
            {
                throw new FormattingProfileException("$.wrap.maxWidth", "deve ser de ao menos 1 coluna.");
            }

            if (wrap.MaxLines is < 1)
            {
                throw new FormattingProfileException("$.wrap.maxLines", "deve ser de ao menos 1 linha, ou null para não limitar.");
            }

            wrapReader.EnsureNoUnknownProperties();
        }

        var replacements = defaults.Replacements;
        if (reader.OptionalObject("replacements") is { } replacementsReader)
        {
            replacements = new ReplacementRules(
                replacementsReader.OptionalReplacements("before") ?? [],
                replacementsReader.OptionalReplacements("after") ?? []);
            replacementsReader.EnsureNoUnknownProperties();
        }

        reader.EnsureNoUnknownProperties();
        return new FormattingProfile(kinds, wrap) { Replacements = replacements };
    }

    private static string KindName(TextUnitKind kind) => kind.ToString().ToLowerInvariant();

    private static string CharWidthName(CharWidthMode mode) => mode switch
    {
        CharWidthMode.Uniform => "uniform",
        _ => "halfWidthLatin",
    };

    private sealed class ObjectReader
    {
        private readonly JsonElement _element;
        private readonly string _path;
        private readonly HashSet<string> _read = [];

        public ObjectReader(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormattingProfileException(path, "deve ser um objeto JSON.");
            }

            _element = element;
            _path = path;
        }

        public bool HasProperty(string name) => _element.TryGetProperty(name, out _);

        public bool? OptionalBool(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new FormattingProfileException(PathOf(name), "deve ser true ou false."),
            };
        }

        /// <summary>Número inteiro; <c>null</c> no JSON e campo ausente dão <c>null</c>.</summary>
        public int? OptionalInt(string name, bool allowNull = false)
        {
            if (!TryGet(name, out var value) || (allowNull && value.ValueKind == JsonValueKind.Null))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                throw new FormattingProfileException(PathOf(name), "deve ser um número inteiro.");
            }

            return number;
        }

        public CharWidthMode? OptionalCharWidth(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() switch
                {
                    "halfWidthLatin" => CharWidthMode.HalfWidthLatin,
                    "uniform" => CharWidthMode.Uniform,
                    _ => throw new FormattingProfileException(PathOf(name), "use \"halfWidthLatin\" ou \"uniform\"."),
                }
                : throw new FormattingProfileException(PathOf(name), "deve ser texto.");
        }

        public IReadOnlyList<TextUnitKind>? OptionalKinds(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new FormattingProfileException(PathOf(name), "deve ser uma lista de papéis de linha.");
            }

            var kinds = new List<TextUnitKind>();
            foreach (var item in value.EnumerateArray())
            {
                var itemPath = $"{PathOf(name)}[{kinds.Count}]";
                if (item.ValueKind != JsonValueKind.String || !TryParseKind(item.GetString()!, out var kind))
                {
                    throw new FormattingProfileException(itemPath, $"use um destes: {string.Join(", ", Enum.GetValues<TextUnitKind>().Select(KindName))}.");
                }

                kinds.Add(kind);
            }

            return kinds.Count == 0
                ? throw new FormattingProfileException(PathOf(name), "informe ao menos um papel de linha.")
                : kinds;
        }

        public string RequiredString(string name)
        {
            if (!TryGet(name, out var value))
            {
                throw new FormattingProfileException(PathOf(name), "campo obrigatório.");
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : throw new FormattingProfileException(PathOf(name), "deve ser texto.");
        }

        public IReadOnlyList<Replacement>? OptionalReplacements(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new FormattingProfileException(PathOf(name), "deve ser uma lista de substituições.");
            }

            var replacements = new List<Replacement>();
            foreach (var item in value.EnumerateArray())
            {
                var itemReader = new ObjectReader(item, $"{PathOf(name)}[{replacements.Count}]");
                var replacement = new Replacement(
                    itemReader.RequiredString("pattern"),
                    itemReader.RequiredString("with"),
                    itemReader.OptionalBool("regex") ?? false);
                itemReader.EnsureNoUnknownProperties();
                if (ReplacementStage.Validate(replacement) is { } error)
                {
                    throw new FormattingProfileException(itemReader.PathOf("pattern"), error);
                }

                replacements.Add(replacement);
            }

            return replacements;
        }

        public ObjectReader? OptionalObject(string name) =>
            TryGet(name, out var value) ? new ObjectReader(value, PathOf(name)) : null;

        public void EnsureNoUnknownProperties()
        {
            foreach (var property in _element.EnumerateObject())
            {
                if (!_read.Contains(property.Name))
                {
                    throw new FormattingProfileException(PathOf(property.Name), "campo desconhecido.");
                }
            }
        }

        private static bool TryParseKind(string text, out TextUnitKind kind)
        {
            foreach (var candidate in Enum.GetValues<TextUnitKind>())
            {
                if (string.Equals(KindName(candidate), text, StringComparison.Ordinal))
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = default;
            return false;
        }

        private bool TryGet(string name, out JsonElement value)
        {
            _read.Add(name);
            return _element.TryGetProperty(name, out value);
        }

        public string PathOf(string name) =>
            name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? $"{_path}.{name}" : $"{_path}['{name}']";
    }
}
