using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Glossary;

namespace VNForge.Core.Bible;

/// <summary>
/// Lê e grava o <c>bible.json</c>: <c>version</c>, a geração, a sinopse, a linha do tempo e uma cena por chave de script. Os campos que a pessoa
/// escreveu vão em <c>edited</c>, por cena. UTF-8 sem BOM, LF, japonês sem escape.
/// </summary>
public static class BibleSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <exception cref="GlossaryFileException">Cena sem chave, repetida ou com campo editado repetido.</exception>
    public static string Serialize(GameBible bible, string filePath = "(memória)")
    {
        ArgumentNullException.ThrowIfNull(bible);
        Check(bible, filePath);

        var file = new FileDto
        {
            Version = CurrentVersion,
            Generation = bible.Generation is { } g ? new GenerationDto(g.At, g.Model, g.ScenesRead, g.Chapters, g.Dollars) : null,
            Synopsis = new SynopsisDto(bible.Synopsis, bible.SynopsisEdited),
            Timeline = new TimelineDto([.. bible.Timeline.Select(t => new TimelineEntryDto(t.Title, t.Note))], bible.TimelineEdited),
            Scenes = [.. bible.Scenes.Select(ToDto)],
        };
        return JsonSerializer.Serialize(file, Options) + "\n";
    }

    /// <exception cref="GlossaryFileException">JSON malformado, campo desconhecido ou ausente, versão desconhecida, cena sem chave ou repetida.</exception>
    public static GameBible Parse(string json, string filePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(filePath);

        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new GlossaryFileException(filePath, Describe(ex), ex);
        }

        if (file is null)
        {
            throw new GlossaryFileException(filePath, "o arquivo está vazio.");
        }

        if (file.Version > CurrentVersion)
        {
            throw new GlossaryFileException(filePath, $"$.version: a versão {file.Version} é mais nova que a {CurrentVersion}, a única que este VNForge lê; atualize o VNForge.");
        }

        if (file.Version != CurrentVersion)
        {
            throw new GlossaryFileException(filePath, $"$.version: a versão {file.Version} não existe; o valor esperado é {CurrentVersion}.");
        }

        var bible = new GameBible
        {
            Synopsis = file.Synopsis.Text,
            SynopsisEdited = file.Synopsis.Edited,
            Timeline = [.. file.Timeline.Entries.Select(t => new BibleTimelineEntry(t.Title, t.Note))],
            TimelineEdited = file.Timeline.Edited,
            Generation = file.Generation is { } g ? new BibleGeneration(g.At, g.Model, g.ScenesRead, g.Chapters, g.Dollars) : null,
            Scenes = [.. file.Scenes.Select(FromDto)],
        };
        Check(bible, filePath);
        return GameBible.From(bible.Scenes) with
        {
            Synopsis = bible.Synopsis,
            SynopsisEdited = bible.SynopsisEdited,
            Timeline = bible.Timeline,
            TimelineEdited = bible.TimelineEdited,
            Generation = bible.Generation,
        };
    }

    private static void Check(GameBible bible, string filePath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < bible.Scenes.Count; i++)
        {
            var scene = bible.Scenes[i] ?? throw new GlossaryFileException(filePath, $"$.scenes[{i}]: a cena é nula.");
            if (string.IsNullOrWhiteSpace(scene.Key) || scene.Key != scene.Key.Trim().ToLowerInvariant())
            {
                throw new GlossaryFileException(filePath, $"$.scenes[{i}].key: a chave '{scene.Key}' é inválida; use minúsculas e sem espaços nas pontas.");
            }

            if (!seen.Add(scene.Key))
            {
                throw new GlossaryFileException(filePath, $"A cena '{scene.Key}' aparece mais de uma vez no arquivo.");
            }

            if (scene.Edited.Distinct().Count() != scene.Edited.Count)
            {
                throw new GlossaryFileException(filePath, $"$.scenes[{i}].edited: um campo aparece mais de uma vez.");
            }
        }
    }

    private static SceneDto ToDto(BibleScene s) => new()
    {
        Key = s.Key,
        Name = s.Name,
        Summary = s.Summary,
        Characters = [.. s.Characters],
        Tone = [.. s.Tone],
        Jokes = [.. s.Jokes],
        Relations = [.. s.Relations.Select(r => new RelationDto(r.Between, r.Note))],
        Edited = [.. s.Edited],
    };

    private static BibleScene FromDto(SceneDto s) => new(s.Key)
    {
        Name = s.Name,
        Summary = s.Summary,
        Characters = [.. s.Characters],
        Tone = [.. s.Tone],
        Jokes = [.. s.Jokes],
        Relations = [.. s.Relations.Select(r => new BibleRelation(r.Between, r.Note))],
        Edited = [.. s.Edited.Distinct().Order()],
    };

    private static string Describe(JsonException ex)
    {
        var detail = ex.Message;
        var cut = detail.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut >= 0)
        {
            detail = detail[..cut];
        }

        var kind = detail.Contains("could not be mapped", StringComparison.Ordinal) ? "campo desconhecido."
            : detail.Contains("missing required", StringComparison.Ordinal) ? "campo obrigatório ausente."
            : detail.Contains("null value", StringComparison.Ordinal) ? "o campo não aceita null."
            : "JSON malformado ou valor de tipo errado.";
        var where = ex.Path is { Length: > 0 } path ? $"{path}: " : string.Empty;
        var position = ex.LineNumber is { } line && ex.BytePositionInLine is { } column ? $"linha {line + 1}, coluna {column + 1}: " : string.Empty;
        return where + position + kind;
    }

    private sealed class FileDto
    {
        [JsonRequired]
        public int Version { get; set; }

        public GenerationDto? Generation { get; set; }

        public SynopsisDto Synopsis { get; set; } = new(string.Empty, false);

        public TimelineDto Timeline { get; set; } = new([], false);

        public List<SceneDto> Scenes { get; set; } = [];
    }

    private sealed record GenerationDto(
        [property: JsonRequired] DateTimeOffset At,
        [property: JsonRequired] string Model,
        [property: JsonRequired] int ScenesRead,
        [property: JsonRequired] int Chapters,
        [property: JsonRequired] decimal Dollars);

    private sealed record SynopsisDto([property: JsonRequired] string Text, bool Edited);

    private sealed record TimelineDto([property: JsonRequired] List<TimelineEntryDto> Entries, bool Edited);

    private sealed record TimelineEntryDto([property: JsonRequired] string Title, [property: JsonRequired] string Note);

    private sealed record RelationDto([property: JsonRequired] string Between, [property: JsonRequired] string Note);

    private sealed class SceneDto
    {
        [JsonRequired]
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public List<string> Characters { get; set; } = [];

        public List<string> Tone { get; set; } = [];

        public List<string> Jokes { get; set; } = [];

        public List<RelationDto> Relations { get; set; } = [];

        public List<BibleField> Edited { get; set; } = [];
    }
}
