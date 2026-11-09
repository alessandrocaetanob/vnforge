using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>O que um ajuste da passada de editor melhora. Os nomes gravados no JSON entram também no schema do pedido (<c>ClaudeEditorPassProvider.Schema</c>).</summary>
[JsonConverter(typeof(StrictEnumConverter<EditorSuggestionKind>))]
public enum EditorSuggestionKind
{
    /// <summary>A frase soa travada, presa à ordem do original ou pesada em português.</summary>
    [JsonStringEnumMemberName("fluency")]
    Fluency,

    /// <summary>A fala não soa como o personagem: tratamento, registro, jeito de falar.</summary>
    [JsonStringEnumMemberName("voice")]
    Voice,

    /// <summary>O mesmo termo, piada ou tratamento aparece traduzido de outro jeito em outra linha da cena.</summary>
    [JsonStringEnumMemberName("consistency")]
    Consistency,
}

/// <summary>
/// Um ajuste que a segunda passada de editor propôs para uma linha já traduzida. É só sugestão: a tradução da linha não muda até a pessoa aceitar.
/// </summary>
/// <param name="ScriptKey">A chave do script (a cena) da linha.</param>
/// <param name="UnitId">O id da linha.</param>
/// <param name="Kind">O que o ajuste melhora.</param>
/// <param name="Base">A tradução que a IA leu ao propor. Se a linha mudar depois, a sugestão deixa de valer (ver <see cref="EditorSuggestionsFile.For"/>).</param>
/// <param name="Proposed">O texto proposto, com os mesmos marcadores de controle da tradução.</param>
/// <param name="Reason">O motivo, em uma frase.</param>
/// <param name="At">Quando a passada rodou.</param>
public sealed record EditorSuggestion(
    string ScriptKey,
    string UnitId,
    EditorSuggestionKind Kind,
    string Base,
    string Proposed,
    string Reason,
    DateTimeOffset At)
{
    /// <summary>
    /// A sugestão ainda vale para <paramref name="unit"/>: a linha tem a tradução em que a IA se baseou e não está aprovada. Editar o texto por qualquer caminho a aposenta, e aprovar
    /// a linha é a pessoa dizer que ela está boa como está.
    /// </summary>
    public bool IsLiveFor(TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Status != TranslationStatus.Approved
            && string.Equals(UnitId, unit.Id, StringComparison.Ordinal)
            && string.Equals(Base, unit.Translation, StringComparison.Ordinal);
    }

    /// <summary>O nome do tipo para mostrar ao usuário.</summary>
    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        EditorSuggestionKind.Fluency => "fluidez",
        EditorSuggestionKind.Voice => "voz",
        EditorSuggestionKind.Consistency => "consistência",
        _ => Kind.ToString(),
    };
}

/// <summary>
/// As sugestões da passada de editor em <c>.cache/editor-suggestions.json</c>. Ficam fora do <c>text/*.json</c> de propósito: a sugestão guarda a tradução em que foi
/// feita e só vale enquanto a linha continua com ela, então editar a linha por qualquer caminho a aposenta sem que o gravador de linhas precise saber dela.
/// Apagar o <c>.cache/</c> perde só as sugestões pendentes, nenhuma tradução.
/// </summary>
public sealed record EditorSuggestionsFile(int Version, IReadOnlyList<EditorSuggestion> Suggestions)
{
    public const int CurrentVersion = 1;

    private const string FileName = "editor-suggestions.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static EditorSuggestionsFile Empty { get; } = new(CurrentVersion, []);

    public static string PathFor(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Path.Combine(project.CacheDirectory, FileName);
    }

    /// <summary>As sugestões gravadas, ou <see cref="Empty"/> quando não há arquivo, ele não abre ou não é legível: o Editor não trava por causa dele.</summary>
    public static EditorSuggestionsFile Load(ProjectFolder project)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(ProjectFileIo.ReadAllBytes(PathFor(project)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }

        try
        {
            var read = JsonSerializer.Deserialize<EditorSuggestionsFile>(text, Json);
            return read is { Version: CurrentVersion, Suggestions: not null }
                ? read with { Suggestions = [.. read.Suggestions.Where(s => s is { ScriptKey: not null, UnitId: not null, Base: not null, Proposed: not null, Reason: not null })] }
                : Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>Grava o arquivo de forma atômica (arquivo temporário e troca). Sem sugestões, apaga o arquivo. Falha com <see cref="DirectoryNotFoundException"/> se a pasta do projeto sumiu.</summary>
    public static void Save(ProjectFolder project, EditorSuggestionsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = PathFor(project);
        if (!Directory.Exists(project.Root))
        {
            throw new DirectoryNotFoundException($"A pasta do projeto '{project.Root}' não existe mais; as sugestões do editor não foram gravadas.");
        }

        if (file.Suggestions.Count == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(project.CacheDirectory);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(file with { Version = CurrentVersion }, Json) + "\n", new UTF8Encoding(false));
            ProjectFileIo.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>A sugestão que vale para a linha (<see cref="EditorSuggestion.IsLiveFor"/>); nula sem sugestão, quando a linha mudou desde a passada ou quando foi aprovada.</summary>
    public EditorSuggestion? For(string scriptKey, TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return Suggestions.FirstOrDefault(s => string.Equals(s.ScriptKey, scriptKey, StringComparison.Ordinal) && s.IsLiveFor(unit));
    }

    /// <summary>As sugestões que ainda valem nas linhas de <paramref name="units"/>, na ordem das linhas.</summary>
    public IReadOnlyList<EditorSuggestion> LiveIn(string scriptKey, IEnumerable<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return [.. units.Select(u => For(scriptKey, u)).OfType<EditorSuggestion>()];
    }

    /// <summary>O arquivo com <paramref name="replacement"/> no lugar de tudo que havia para <paramref name="scriptKey"/>: uma passada nova substitui a anterior da cena.</summary>
    public EditorSuggestionsFile ReplacingScript(string scriptKey, IEnumerable<EditorSuggestion> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return this with { Suggestions = [.. Suggestions.Where(s => !string.Equals(s.ScriptKey, scriptKey, StringComparison.Ordinal)), .. replacement] };
    }

    /// <summary>O arquivo com <paramref name="suggestion"/> no lugar da que havia para a mesma linha ("Outra").</summary>
    public EditorSuggestionsFile Replacing(EditorSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        return this with { Suggestions = [.. Without(suggestion.ScriptKey, suggestion.UnitId).Suggestions, suggestion] };
    }

    /// <summary>O arquivo sem as sugestões da linha (aceita ou ignorada); sem elas, devolve o próprio arquivo.</summary>
    public EditorSuggestionsFile Without(string scriptKey, string unitId)
    {
        var kept = Suggestions.Where(s => !(string.Equals(s.ScriptKey, scriptKey, StringComparison.Ordinal) && string.Equals(s.UnitId, unitId, StringComparison.Ordinal))).ToList();
        return kept.Count == Suggestions.Count ? this : this with { Suggestions = kept };
    }

    /// <summary>O arquivo só com as sugestões de <paramref name="scriptKey"/>, por id de linha: a abertura de uma cena no Editor procura uma por linha.</summary>
    public IReadOnlyDictionary<string, EditorSuggestion> ByUnit(string scriptKey)
    {
        var map = new Dictionary<string, EditorSuggestion>(StringComparer.Ordinal);
        foreach (var s in Suggestions.Where(s => string.Equals(s.ScriptKey, scriptKey, StringComparison.Ordinal)))
        {
            map[s.UnitId] = s;
        }

        return map;
    }
}
