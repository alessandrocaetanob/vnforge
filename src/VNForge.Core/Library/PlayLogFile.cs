using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As sessões de jogo em <c>playtime.json</c>, ao lado do <c>library.json</c>. O tempo de jogo é histórico que não se refaz, então, como na biblioteca,
/// um arquivo ilegível não vira lista vazia: <see cref="Load"/> recusa em vez de deixar o próximo <see cref="Save"/> sobrescrevê-lo.
/// </summary>
public sealed class PlayLogFile(string directory)
{
    public const string FileName = "playtime.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>As sessões do arquivo; vazio quando o arquivo não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public PlayLog Load()
    {
        if (!File.Exists(Path))
        {
            return new PlayLog();
        }

        try
        {
            return Parse(File.ReadAllText(Path), Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"O arquivo '{Path}' não pôde ser lido: {e.Message}", e);
        }
    }

    public void Save(PlayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        AtomicFile.WriteAllText(Path, Serialize(log));
    }

    /// <summary>
    /// Soma uma sessão ao arquivo. Relê o arquivo antes de gravar, então uma sessão de outro jogo que terminou enquanto esta rodava (outra janela, a CLI) não se perde.
    /// </summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido.</exception>
    public PlayLog Record(string gameId, PlaySession session)
    {
        var log = Load();
        log.Add(gameId, session);
        Save(log);
        return log;
    }

    public static string Serialize(PlayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var games = log.All.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(new Envelope(CurrentVersion, games), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, jogo sem id ou sessão que termina antes de começar.</exception>
    public static PlayLog Parse(string json, string filePath = "(memória)")
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
        }
        catch (JsonException e)
        {
            throw new LibraryException($"O arquivo '{filePath}' não está no formato esperado: {e.Message}", e);
        }

        if (envelope is null)
        {
            throw new LibraryException($"O arquivo '{filePath}' está vazio.");
        }

        if (envelope.Version != CurrentVersion)
        {
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} do tempo de jogo; esta versão do VNForge lê a {CurrentVersion}.");
        }

        var log = new PlayLog();
        foreach (var (id, sessions) in envelope.Games ?? [])
        {
            foreach (var session in sessions ?? [])
            {
                if (string.IsNullOrWhiteSpace(id) || session is null || session.EndedAt < session.StartedAt || session.Duration < TimeSpan.Zero)
                {
                    throw new LibraryException($"O arquivo '{filePath}': uma sessão do jogo '{id}' está sem id, com duração negativa ou termina antes de começar.");
                }

                log.Add(id, session);
            }
        }

        return log;
    }

    private sealed record Envelope(int Version, Dictionary<string, List<PlaySession>>? Games);
}
