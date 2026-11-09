using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Em que ponto está um lote noturno guardado em <c>.cache/lote-noturno/</c>.</summary>
public enum NightBatchPhase
{
    /// <summary>Enviado à API, esperando o resultado.</summary>
    Processing,

    /// <summary>A pessoa mandou cancelar; a API ainda encerra o lote e devolve o que já tinha pronto.</summary>
    Canceling,

    /// <summary>O resultado já foi gravado em <c>text/*.json</c>.</summary>
    Applied,
}

/// <summary>
/// Como a linha estava quando o lote saiu. O resultado pode chegar horas depois: só a linha que continua assim é gravada, e a que a pessoa mexeu
/// nesse meio tempo fica como ela deixou.
/// </summary>
/// <param name="TranslationDigest">O hash da tradução; <see langword="null"/> quando a linha não tinha tradução.</param>
public sealed record NightBatchLine(string Id, TranslationStatus Status, string SourceDigest, string? TranslationDigest)
{
    public static NightBatchLine Of(TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return new NightBatchLine(unit.Id, unit.Status, SourceHash.Of(unit.Source), unit.Translation is null ? null : SourceHash.Of(unit.Translation));
    }

    /// <summary>A linha continua como na hora do envio.</summary>
    public bool Matches(TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return Status == unit.Status
            && string.Equals(SourceDigest, SourceHash.Of(unit.Source), StringComparison.Ordinal)
            && string.Equals(TranslationDigest, unit.Translation is null ? null : SourceHash.Of(unit.Translation), StringComparison.Ordinal);
    }
}

/// <summary>Um pedido do lote (um <c>custom_id</c> da API): as linhas que ele traduz.</summary>
public sealed record NightBatchRequest(string CustomId, IReadOnlyList<NightBatchLine> Lines);

/// <summary>O que a gravação do resultado fez.</summary>
/// <param name="Translated">Linhas gravadas como tradução da IA.</param>
/// <param name="Failed">Linhas cujo pedido voltou com erro, expirou ou foi cancelado.</param>
/// <param name="Refused">Linhas que a IA recusou ou que não passaram na validação.</param>
/// <param name="Skipped">Linhas que a pessoa mudou depois do envio e que a gravação não tocou.</param>
/// <param name="RestorePointId">O ponto de restauração do "Desfazer"; <see langword="null"/> quando nenhuma linha mudou.</param>
/// <param name="RestorePointLines">Quantas linhas o ponto guarda.</param>
/// <param name="Dollars">O que o lote custou, em dólar, já com o desconto do lote.</param>
/// <param name="UnfinishedIds">Os ids das linhas que seguem sem tradução (erro ou recusa), para a pessoa vê-las.</param>
public sealed record NightBatchOutcome(int Translated, int Failed, int Refused, int Skipped, string? RestorePointId, int RestorePointLines, decimal Dollars, IReadOnlyList<string> UnfinishedIds);

/// <summary>
/// O lote que a pessoa mandou para a noite: o id da API, o que foi enviado e, depois que o resultado chegou, o que foi gravado. Fica em
/// <c>.cache/lote-noturno/</c> para o app e o <c>vnf</c> retomarem depois de fechados.
/// </summary>
/// <param name="Id">O id do lote na API (<c>msgbatch_...</c>).</param>
/// <param name="Title">O escopo, em português ("O capítulo").</param>
/// <param name="Quality">O nome da qualidade escolhida.</param>
/// <param name="EstimatedBrl">O preço do pedido na hora do envio, com o desconto do lote.</param>
public sealed record NightBatchState(
    string Id,
    string Title,
    string Model,
    string Quality,
    string Effort,
    DateTimeOffset SubmittedAt,
    decimal EstimatedBrl,
    IReadOnlyList<string> ScriptKeys,
    IReadOnlyList<NightBatchRequest> Requests)
{
    public NightBatchPhase Phase { get; init; } = NightBatchPhase.Processing;

    public DateTimeOffset? EndedAt { get; init; }

    public NightBatchOutcome? Outcome { get; init; }

    public int LineCount => Requests.Sum(r => r.Lines.Count);

    public bool IsOpen => Phase != NightBatchPhase.Applied;
}

/// <summary>Lê e grava os lotes noturnos do projeto, um arquivo por lote.</summary>
public sealed class NightBatchStore(ProjectFolder project)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string StoreDirectory => Path.Combine(project.CacheDirectory, "lote-noturno");

    /// <summary>Todos os lotes do projeto, do mais novo para o mais antigo. Arquivo ilegível fica de fora.</summary>
    public IReadOnlyList<NightBatchState> List()
    {
        if (!System.IO.Directory.Exists(StoreDirectory))
        {
            return [];
        }

        var states = new List<NightBatchState>();
        foreach (var path in System.IO.Directory.EnumerateFiles(StoreDirectory, "*.json"))
        {
            if (TryRead(path) is { } state)
            {
                states.Add(state);
            }
        }

        return [.. states.OrderByDescending(s => s.SubmittedAt)];
    }

    /// <summary>Os lotes que ainda esperam o resultado ou a gravação dele.</summary>
    public IReadOnlyList<NightBatchState> Open() => [.. List().Where(s => s.IsOpen)];

    public NightBatchState? Find(string id) => TryRead(PathOf(id));

    public void Save(NightBatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        System.IO.Directory.CreateDirectory(StoreDirectory);
        var path = PathOf(state.Id);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json) + "\n", new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    /// <summary>
    /// O arquivo de trava de um lote: quem grava o resultado o abre com <see cref="FileShare.None"/>, então o app e o <c>vnf</c> não gravam o mesmo lote juntos.
    /// O arquivo fica na pasta depois do uso; só a abertura exclusiva vale como trava.
    /// </summary>
    public string ClaimPath(string id) => Path.ChangeExtension(PathOf(id), ".apply.lock");

    /// <summary>Tenta reivindicar a gravação do lote; <see langword="null"/> quando outro processo ou thread já a reivindicou. Descarte o resultado para soltar a trava.</summary>
    public FileStream? TryClaim(string id)
    {
        System.IO.Directory.CreateDirectory(StoreDirectory);
        try
        {
            return new FileStream(ClaimPath(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string PathOf(string id)
    {
        var safe = new string([.. id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')]);
        return Path.Combine(StoreDirectory, safe + ".json");
    }

    private static NightBatchState? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<NightBatchState>(File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true)), Json) : null;
        }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
