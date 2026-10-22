using System.Text.Encodings.Web;
using System.Text.Json;

namespace VNForge.Core.Projects;

public static class ProjectConfigSerializer
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

    public static ProjectConfig Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            var onde = ex.LineNumber is { } linha ? $"linha {linha + 1}" : "posição desconhecida";
            throw new ProjectConfigException("$", $"JSON malformado ({onde}): {ex.Message}");
        }

        using (document)
        {
            var config = ReadConfig(document.RootElement);
            ProjectConfigValidator.Validate(config);
            return config;
        }
    }

    public static string Serialize(ProjectConfig config)
    {
        ProjectConfigValidator.Validate(config);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("name", config.Name);

            writer.WriteStartObject("game");
            writer.WriteString("path", config.Game.Path);
            writer.WriteString("exe", config.Game.Exe);
            writer.WriteEndObject();

            writer.WriteStartObject("engine");
            writer.WriteString("id", config.Engine.Id);
            if (config.Engine.Version is { } version)
            {
                writer.WriteNumber("version", version);
            }

            if (config.Engine.ScriptKey is { } scriptKey)
            {
                writer.WriteString("scriptKey", scriptKey);
            }

            writer.WriteEndObject();

            writer.WriteStartObject("languages");
            writer.WriteString("source", config.Languages.Source);
            writer.WriteString("target", config.Languages.Target);
            writer.WriteEndObject();

            writer.WriteStartObject("encoding");
            writer.WriteString("strategy", config.Encoding.Strategy);
            writer.WriteEndObject();

            writer.WriteStartObject("ai");
            writer.WriteString("provider", config.Ai.Provider);
            writer.WriteString("model", config.Ai.Model);
            writer.WriteString("effort", config.Ai.Effort);
            writer.WriteNumber("batchSize", config.Ai.BatchSize);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static ProjectConfig ReadConfig(JsonElement root)
    {
        var reader = new ObjectReader(root, "$");

        var name = reader.RequiredString("name");

        var game = reader.RequiredObject("game");
        var gamePath = game.RequiredString("path");
        var gameExe = game.RequiredString("exe");
        game.EnsureNoUnknownProperties();

        var engine = reader.RequiredObject("engine");
        var engineId = engine.RequiredString("id");
        var engineVersion = engine.OptionalInt("version");
        var scriptKey = engine.OptionalString("scriptKey");
        engine.EnsureNoUnknownProperties();

        var languages = reader.RequiredObject("languages");
        var source = languages.RequiredString("source");
        var target = languages.RequiredString("target");
        languages.EnsureNoUnknownProperties();

        var encoding = reader.RequiredObject("encoding");
        var strategy = encoding.RequiredString("strategy");
        encoding.EnsureNoUnknownProperties();

        var ai = AiConfig.Default;
        if (reader.OptionalObject("ai") is { } aiReader)
        {
            ai = new AiConfig(
                aiReader.OptionalString("provider") ?? ai.Provider,
                aiReader.OptionalString("model") ?? ai.Model,
                aiReader.OptionalString("effort") ?? ai.Effort,
                aiReader.OptionalInt("batchSize") ?? ai.BatchSize);
            aiReader.EnsureNoUnknownProperties();
        }

        reader.EnsureNoUnknownProperties();

        return new ProjectConfig(
            name,
            new GameConfig(gamePath, gameExe),
            new EngineConfig(engineId, engineVersion, scriptKey),
            new LanguagesConfig(source, target),
            new EncodingConfig(strategy),
            ai);
    }

    private sealed class ObjectReader
    {
        private readonly JsonElement _element;
        private readonly string _path;
        private readonly HashSet<string> _read = [];

        public ObjectReader(JsonElement element, string path)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ProjectConfigException(path, "deve ser um objeto JSON.");
            }

            _element = element;
            _path = path;
        }

        public string RequiredString(string name) =>
            OptionalString(name) ?? throw Missing(name);

        public string? OptionalString(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new ProjectConfigException(PathOf(name), "deve ser texto.");
            }

            return value.GetString();
        }

        public int? OptionalInt(string name)
        {
            if (!TryGet(name, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                throw new ProjectConfigException(PathOf(name), "deve ser um número inteiro.");
            }

            return number;
        }

        public ObjectReader RequiredObject(string name) =>
            OptionalObject(name) ?? throw Missing(name);

        public ObjectReader? OptionalObject(string name) =>
            TryGet(name, out var value) ? new ObjectReader(value, PathOf(name)) : null;

        public void EnsureNoUnknownProperties()
        {
            foreach (var property in _element.EnumerateObject())
            {
                if (_read.Contains(property.Name))
                {
                    continue;
                }

                var path = PathOf(property.Name);
                if (property.Name.Equals("apiKey", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProjectConfigException(
                        path,
                        "a chave de API não pode ficar no projeto; use a variável de ambiente ANTHROPIC_API_KEY ou o cofre do usuário.");
                }

                throw new ProjectConfigException(path, "campo desconhecido.");
            }
        }

        private bool TryGet(string name, out JsonElement value)
        {
            _read.Add(name);
            return _element.TryGetProperty(name, out value);
        }

        private ProjectConfigException Missing(string name) =>
            new(PathOf(name), "campo obrigatório ausente.");

        private string PathOf(string name) => $"{_path}.{name}";
    }
}
