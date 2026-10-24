using System.Text.Encodings.Web;
using System.Text.Json;
using VNForge.Core.Text;

namespace VNForge.Core.Formatting;

/// <summary>Lê e grava o <c>formatting.json</c>. Campo ausente usa o padrão de <see cref="FormattingProfile.Default"/>; campo desconhecido é erro.</summary>
public static class FormattingProfileSerializer
{
    private const string MaxLinesKey = "maxLines";

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
                writer.WriteNumber(MaxLinesKey, maxLines);
            }
            else
            {
                writer.WriteNull(MaxLinesKey);
            }

            writer.WriteString("charWidth", CharWidthName(profile.Wrap.CharWidth));
            writer.WriteEndObject();

            writer.WriteStartObject("normalization");
            writer.WriteBoolean("nfc", profile.Normalization.Nfc);
            writer.WriteBoolean("invisibleSpaces", profile.Normalization.InvisibleSpaces);
            writer.WriteBoolean("nbsp", profile.Normalization.Nbsp);
            writer.WriteEndObject();

            writer.WriteStartObject("spacing");
            writer.WriteBoolean("collapseSpaces", profile.Spacing.CollapseSpaces);
            writer.WriteBoolean("spaceBeforePunctuation", profile.Spacing.SpaceBeforePunctuation);
            writer.WriteBoolean("spaceAfterPunctuation", profile.Spacing.SpaceAfterPunctuation);
            writer.WriteEndObject();

            if (!profile.Replacements.IsEmpty)
            {
                writer.WriteStartObject("replacements");
                WriteReplacements(writer, "before", profile.Replacements.Before);
                WriteReplacements(writer, "after", profile.Replacements.After);
                writer.WriteEndObject();
            }

            writer.WriteStartObject("typography");
            writer.WriteString("quotes", EnumName(profile.Typography.Quotes));
            writer.WriteString("ellipsis", EnumName(profile.Typography.Ellipsis));
            WriteCharOrNull(writer, "dash", profile.Typography.Dash);
            WriteCharOrNull(writer, "dialogueDash", profile.Typography.DialogueDash);
            writer.WriteEndObject();

            writer.WriteStartObject("width");
            writer.WriteString("mode", EnumName(profile.Width.Mode));
            writer.WriteBoolean("spaces", profile.Width.Spaces);
            writer.WriteEndObject();

            if (profile.ByteLimits.Count > 0)
            {
                writer.WriteStartObject("byteLimits");
                foreach (var (kind, limit) in profile.ByteLimits.OrderBy(l => l.Key))
                {
                    writer.WriteNumber(KindName(kind), limit);
                }

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

    // Barra e chaves abririam um marcador; controle e substituto não são um caractere de travessão.
    private static bool IsValidDash(char c) => !char.IsControl(c) && !char.IsSurrogate(c) && c is not ('\\' or '{' or '}');

    private static void WriteCharOrNull(Utf8JsonWriter writer, string name, char? value)
    {
        if (value is { } c)
        {
            writer.WriteString(name, c.ToString());
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string EnumName<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

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

        foreach (var (kind, limit) in profile.ByteLimits)
        {
            if (limit < 1)
            {
                throw new FormattingProfileException($"$.byteLimits.{KindName(kind)}", "deve ser de ao menos 1 byte.");
            }
        }

        if (profile.Typography.Dash is { } dash && !IsValidDash(dash))
        {
            throw new FormattingProfileException("$.typography.dash", "deve ser um caractere visível, fora \\, { e }.");
        }

        if (profile.Typography.DialogueDash is { } dialogueDash && !IsValidDash(dialogueDash))
        {
            throw new FormattingProfileException("$.typography.dialogueDash", "deve ser um caractere visível, fora \\, { e }.");
        }
    }

    private static FormattingProfile ReadProfile(JsonElement root)
    {
        var reader = new ObjectReader(root, "$");
        var defaults = FormattingProfile.Default;

        var kinds = reader.OptionalKinds("kinds") ?? defaults.Kinds;

        var wrap = ReadWrap(reader, defaults.Wrap);

        var normalization = ReadNormalization(reader, defaults.Normalization);

        var spacing = ReadSpacing(reader, defaults.Spacing);

        var replacements = ReadReplacements(reader, defaults.Replacements);

        var typography = ReadTypography(reader, defaults.Typography);

        var width = ReadWidth(reader, defaults.Width);

        var byteLimits = reader.OptionalObject("byteLimits") is { } limitsReader ? limitsReader.ByteLimits() : defaults.ByteLimits;

        reader.EnsureNoUnknownProperties();
        return new FormattingProfile(kinds, wrap) { Normalization = normalization, Spacing = spacing, Replacements = replacements, Typography = typography, Width = width, ByteLimits = byteLimits };
    }

    private static WrapRule ReadWrap(ObjectReader reader, WrapRule current)
    {
        var wrap = current;
        if (reader.OptionalObject("wrap") is { } wrapReader)
        {
            wrap = new WrapRule(
                wrapReader.OptionalBool("enabled") ?? wrap.Enabled,
                wrapReader.OptionalInt("maxWidth") ?? wrap.MaxWidth,
                wrapReader.HasProperty(MaxLinesKey) ? wrapReader.OptionalInt(MaxLinesKey, allowNull: true) :wrap.MaxLines,
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

        return wrap;
    }

    private static NormalizationRule ReadNormalization(ObjectReader reader, NormalizationRule current)
    {
        var normalization = current;
        if (reader.OptionalObject("normalization") is { } normalizationReader)
        {
            normalization = new NormalizationRule(
                normalizationReader.OptionalBool("nfc") ?? normalization.Nfc,
                normalizationReader.OptionalBool("invisibleSpaces") ?? normalization.InvisibleSpaces,
                normalizationReader.OptionalBool("nbsp") ?? normalization.Nbsp);
            normalizationReader.EnsureNoUnknownProperties();
        }

        return normalization;
    }

    private static SpacingRule ReadSpacing(ObjectReader reader, SpacingRule current)
    {
        var spacing = current;
        if (reader.OptionalObject("spacing") is { } spacingReader)
        {
            spacing = new SpacingRule(
                spacingReader.OptionalBool("collapseSpaces") ?? spacing.CollapseSpaces,
                spacingReader.OptionalBool("spaceBeforePunctuation") ?? spacing.SpaceBeforePunctuation,
                spacingReader.OptionalBool("spaceAfterPunctuation") ?? spacing.SpaceAfterPunctuation);
            spacingReader.EnsureNoUnknownProperties();
        }

        return spacing;
    }

    private static ReplacementRules ReadReplacements(ObjectReader reader, ReplacementRules current)
    {
        var replacements = current;
        if (reader.OptionalObject("replacements") is { } replacementsReader)
        {
            replacements = new ReplacementRules(
                replacementsReader.OptionalReplacements("before") ?? [],
                replacementsReader.OptionalReplacements("after") ?? []);
            replacementsReader.EnsureNoUnknownProperties();
        }

        return replacements;
    }

    private static TypographyRule ReadTypography(ObjectReader reader, TypographyRule current)
    {
        var typography = current;
        if (reader.OptionalObject("typography") is { } typographyReader)
        {
            typography = new TypographyRule(
                typographyReader.OptionalEnum<QuoteStyle>("quotes") ?? typography.Quotes,
                typographyReader.OptionalEnum<EllipsisStyle>("ellipsis") ?? typography.Ellipsis,
                typographyReader.HasProperty("dash") ? typographyReader.OptionalChar("dash") : typography.Dash,
                typographyReader.HasProperty("dialogueDash") ? typographyReader.OptionalChar("dialogueDash") : typography.DialogueDash);
            typographyReader.EnsureNoUnknownProperties();
        }

        return typography;
    }

    private static WidthRule ReadWidth(ObjectReader reader, WidthRule current)
    {
        var width = current;
        if (reader.OptionalObject("width") is { } widthReader)
        {
            width = new WidthRule(
                widthReader.OptionalEnum<WidthMode>("mode") ?? width.Mode,
                widthReader.OptionalBool("spaces") ?? width.Spaces);
            widthReader.EnsureNoUnknownProperties();
        }

        return width;
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

        /// <summary>Valor de um enum escrito pelo nome em minúsculas (<c>"ascii"</c>).</summary>
        public T? OptionalEnum<T>(string name)
            where T : struct, Enum
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            var names = Enum.GetValues<T>().Select(v => v.ToString().ToLowerInvariant()).ToArray();
            if (value.ValueKind == JsonValueKind.String)
            {
                foreach (var candidate in Enum.GetValues<T>())
                {
                    if (string.Equals(candidate.ToString().ToLowerInvariant(), value.GetString(), StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }

            throw new FormattingProfileException(PathOf(name), $"use um destes: {string.Join(", ", names)}.");
        }

        /// <summary>Um caractere (texto de um <c>char</c>); <c>null</c> no JSON desliga a regra.</summary>
        public char? OptionalChar(string name)
        {
            if (!TryGet(name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 1 } text && IsValidDash(text[0])
                ? text[0]
                : throw new FormattingProfileException(PathOf(name), "deve ser um único caractere, ou null para não trocar.");
        }

        public List<TextUnitKind>? OptionalKinds(string name)
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

        /// <summary>Objeto <c>{ "name": 16 }</c>: papel de linha e limite de bytes, de ao menos 1.</summary>
        public Dictionary<TextUnitKind, int> ByteLimits()
        {
            var limits = new Dictionary<TextUnitKind, int>();
            foreach (var property in _element.EnumerateObject())
            {
                _read.Add(property.Name);
                if (!TryParseKind(property.Name, out var kind))
                {
                    throw new FormattingProfileException(PathOf(property.Name), $"use um destes papéis: {string.Join(", ", Enum.GetValues<TextUnitKind>().Select(KindName))}.");
                }

                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var limit) || limit < 1)
                {
                    throw new FormattingProfileException(PathOf(property.Name), "deve ser um número inteiro de ao menos 1 byte.");
                }

                limits[kind] = limit;
            }

            return limits;
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

        public List<Replacement>? OptionalReplacements(string name)
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
            var unknown = _element.EnumerateObject().Select(property => property.Name).FirstOrDefault(name => !_read.Contains(name));
            if (unknown is not null)
            {
                throw new FormattingProfileException(PathOf(unknown), "campo desconhecido.");
            }
        }

        private static bool TryParseKind(string text, out TextUnitKind kind)
        {
            var kinds = Enum.GetValues<TextUnitKind>();
            var index = Array.FindIndex(kinds, candidate => string.Equals(KindName(candidate), text, StringComparison.Ordinal));
            if (index >= 0)
            {
                kind = kinds[index];
                return true;
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
