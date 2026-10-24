using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Projects;

/// <summary>Situação de uma etapa do pipeline.</summary>
public enum StageStatus
{
    Completed,
}

/// <summary>Estado de uma etapa gravada em <c>.cache/state.json</c>.</summary>
public sealed record StageState(StageStatus Status);

/// <summary>Um arquivo original do jogo que a extração leu.</summary>
/// <param name="Path">Caminho relativo à pasta do jogo, com <c>/</c>.</param>
/// <param name="Size">Tamanho em bytes.</param>
/// <param name="Sha256">SHA-256 em hexadecimal minúsculo; a E8.5 compara com o arquivo atual para recusar a injeção num jogo que mudou.</param>
public sealed record OriginalFile(string Path, long Size, string Sha256);

/// <summary>
/// Conteúdo do <c>.cache/state.json</c>: os hashes dos originais do jogo, a pasta do jogo que a extração leu (<c>GameDirectory</c>, nula em estado antigo) e o estado de cada etapa, para a CLI e o app continuarem de onde
/// o outro parou. Sem data nem outro valor que mude a cada execução: o mesmo estado gera o mesmo arquivo.
/// </summary>
public sealed record PipelineState(IReadOnlyList<OriginalFile> Originals, IReadOnlyDictionary<string, StageState> Stages, string? GameDirectory = null)
{
    public const int CurrentVersion = 1;

    public static PipelineState Empty { get; } = new([], new Dictionary<string, StageState>());

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    internal string Serialize() =>
        JsonSerializer.Serialize(
            new Document(
                CurrentVersion,
                Originals.OrderBy(o => o.Path, StringComparer.Ordinal).ToList(),
                Stages.OrderBy(s => s.Key, StringComparer.Ordinal).ToDictionary(s => s.Key, s => s.Value),
                GameDirectory),
            Options) + "\n";

    internal static PipelineState Parse(string json, string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Options);
            if (document is null
                || document.Version != CurrentVersion
                || document.Originals is null
                || document.Stages is null
                || document.Originals.Any(o => o is null || string.IsNullOrWhiteSpace(o.Path) || string.IsNullOrWhiteSpace(o.Sha256))
                || document.Stages.Values.Any(s => s is null))
            {
                throw new VNForgeException($"O estado do pipeline está fora do formato esperado: {path}. Apague o .cache/ para recomeçar.");
            }

            return new PipelineState(document.Originals, document.Stages, document.GameDirectory);
        }
        catch (JsonException ex)
        {
            throw new VNForgeException($"O estado do pipeline não pôde ser lido: {path}. Apague o .cache/ para recomeçar.", ex);
        }
    }

    private sealed record Document(int Version, List<OriginalFile> Originals, Dictionary<string, StageState> Stages, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GameDirectory = null);
}
