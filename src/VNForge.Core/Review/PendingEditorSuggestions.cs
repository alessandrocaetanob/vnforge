using System.Text.Json;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Review;

/// <summary>
/// As linhas para as quais a passada de editor (#532) deixou um ajuste em <c>.cache/editor-suggestions.json</c>, lidas só para pontuar o risco. Guarda a tradução
/// em que cada ajuste se baseou: se a linha mudou desde a passada, ou foi aprovada, o ajuste já não vale e não pesa. Sem o arquivo, ou com um que não abre ou não
/// é legível, não há sugestões e a fila segue sem elas.
/// </summary>
public sealed class PendingEditorSuggestions
{
    private const string FileName = "editor-suggestions.json";

    private const int SupportedVersion = 1;

    private readonly Dictionary<(string ScriptKey, string UnitId), string> baseTexts;

    private PendingEditorSuggestions(Dictionary<(string ScriptKey, string UnitId), string> baseTexts) => this.baseTexts = baseTexts;

    public static PendingEditorSuggestions Empty { get; } = new([]);

    public int Count => baseTexts.Count;

    /// <summary>Monta a partir de (chave do script, id da linha, tradução em que a sugestão se baseou); para quem já tem as sugestões em memória.</summary>
    public static PendingEditorSuggestions Of(params (string ScriptKey, string UnitId, string Base)[] suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        var map = new Dictionary<(string ScriptKey, string UnitId), string>();
        foreach (var (scriptKey, unitId, baseText) in suggestions)
        {
            map[(scriptKey, unitId)] = baseText;
        }

        return map.Count == 0 ? Empty : new PendingEditorSuggestions(map);
    }

    /// <summary>Lê as sugestões do projeto; <see cref="Empty"/> quando não há arquivo, ele não abre, é de outra versão ou não é legível.</summary>
    public static PendingEditorSuggestions Load(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        byte[] bytes;
        try
        {
            bytes = ProjectFileIo.ReadAllBytes(Path.Combine(project.CacheDirectory, FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != SupportedVersion
                || !root.TryGetProperty("suggestions", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }

            var map = new Dictionary<(string ScriptKey, string UnitId), string>();
            foreach (var item in list.EnumerateArray())
            {
                if (Text(item, "scriptKey") is { } scriptKey && Text(item, "unitId") is { } unitId && Text(item, "base") is { } baseText)
                {
                    map[(scriptKey, unitId)] = baseText;
                }
            }

            return map.Count == 0 ? Empty : new PendingEditorSuggestions(map);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>O ajuste ainda vale para <paramref name="unit"/>: ela tem a tradução em que a passada se baseou e não está aprovada.</summary>
    public bool Covers(string scriptKey, TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Status != TranslationStatus.Approved
            && baseTexts.TryGetValue((scriptKey, unit.Id), out var baseText)
            && string.Equals(baseText, unit.Translation, StringComparison.Ordinal);
    }

    private static string? Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
