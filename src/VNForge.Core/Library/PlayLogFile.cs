using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As sessões de jogo em <c>playtime.json</c>, ao lado do <c>library.json</c>. O tempo de jogo é histórico que não se refaz, então, como na biblioteca,
/// um arquivo ilegível não vira lista vazia: <see cref="Load"/> recusa em vez de deixar o próximo <see cref="Save"/> sobrescrevê-lo. Uma sessão isolada com
/// duração negativa ou que termina antes de começar é outra coisa: é descartada com um aviso (<see cref="PlayLog.Warnings"/>), sem derrubar o resto.
/// </summary>
public sealed class PlayLogFile(string directory, TimeProvider? clock = null, TimeSpan? lockTimeout = null)
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

    /// <summary>Quanto <see cref="Update{T}(Func{PlayLog, T})"/> espera, por padrão, outro processo soltar a trava.</summary>
    public static TimeSpan DefaultLockTimeout { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Prefixo da cópia do arquivo original que guarda o que o <see cref="Parse"/> descartou: <c>playtime.json.invalido-aaaammdd-hhmmss</c> (UTC).</summary>
    public const string InvalidCopyPrefix = FileName + ".invalido-";

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

    /// <summary>
    /// Quando a leitura descartou alguma sessão (<see cref="PlayLog.Warnings"/>), guarda uma cópia do arquivo original ao lado, em <c>playtime.json.invalido-aaaammdd-hhmmss</c>,
    /// porque a próxima gravação o reescreve sem elas. Só copia uma vez: se uma cópia idêntica já existe, devolve o caminho dela. Devolve <see langword="null"/> quando nada foi descartado.
    /// </summary>
    /// <param name="loaded">O que <see cref="Load"/> devolveu para o arquivo de agora.</param>
    /// <exception cref="LibraryException">A cópia não pôde ser feita; nesse caso nada deve ser regravado.</exception>
    public string? BackUpDiscarded(PlayLog loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (loaded.Warnings.Count == 0 || !File.Exists(Path))
        {
            return null;
        }

        try
        {
            var current = File.ReadAllBytes(Path);
            var folder = System.IO.Path.GetDirectoryName(Path)!;
            var existing = Directory.EnumerateFiles(folder, InvalidCopyPrefix + "*")
                .FirstOrDefault(candidate => File.ReadAllBytes(candidate).AsSpan().SequenceEqual(current));
            if (existing is not null)
            {
                return existing;
            }

            var stamp = (clock ?? TimeProvider.System).GetUtcNow().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var copy = System.IO.Path.Combine(folder, InvalidCopyPrefix + stamp);
            File.Copy(Path, copy, overwrite: true);
            return copy;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Não foi possível guardar uma cópia de '{Path}' antes de descartar sessões inválidas: {e.Message}", e);
        }
    }
    public void Save(PlayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        AtomicFile.WriteAllText(Path, Serialize(log));
    }

    /// <summary>
    /// Lê o arquivo, aplica <paramref name="change"/> e grava, com o arquivo travado (<c>playtime.json.lock</c>): uma sessão que outro processo (outra janela, a CLI)
    /// gravou enquanto esta rodava não se perde.
    /// </summary>
    /// <exception cref="LibraryException">O arquivo está ilegível, ou outro processo o segurou por mais de <see cref="DefaultLockTimeout"/>.</exception>
    public T Update<T>(Func<PlayLog, T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var gate = FileLock.Acquire(Path + ".lock", lockTimeout ?? DefaultLockTimeout, $"O tempo de jogo ('{Path}') está em uso por outro processo. Tente de novo em instantes.");
        var log = Load();
        BackUpDiscarded(log);
        var result = change(log);
        Save(log);
        return result;
    }

    /// <summary>Soma a sessão, ou troca a de mesmo id (a sessão aberta que anda e fecha), sob a trava de <see cref="Update{T}(Func{PlayLog, T})"/>.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido, ou está em uso.</exception>
    public PlayLog Record(string gameId, PlaySession session) =>
        Update(log =>
        {
            log.Upsert(gameId, session);
            return log;
        });

    /// <summary>Fecha como interrompidas as sessões que ficaram abertas sem ninguém atualizá-las (veja <see cref="PlayLog.CloseStale"/>).</summary>
    public int RecoverStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Update(log => log.CloseStale(now, staleAfter));

    public static string Serialize(PlayLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var games = log.All.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(new Envelope(CurrentVersion, games), Json);
    }

    /// <exception cref="LibraryException">JSON malformado ou versão desconhecida.</exception>
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

        var kept = new Dictionary<string, IReadOnlyList<PlaySession>>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var (id, sessions) in envelope.Games ?? [])
        {
            var valid = new List<PlaySession>();
            foreach (var session in sessions ?? [])
            {
                if (string.IsNullOrWhiteSpace(id) || session is null || session.EndedAt < session.StartedAt || session.Duration < TimeSpan.Zero)
                {
                    warnings.Add($"Uma sessão do jogo '{id}' foi descartada: duração negativa ou fim anterior ao começo. Antes da próxima gravação o arquivo original fica copiado ao lado, em {InvalidCopyPrefix}*.");
                    continue;
                }

                // Arquivo de antes dos ids: dá um id à sessão para ela poder ser trocada depois.
                valid.Add(string.IsNullOrWhiteSpace(session.Id) ? session with { Id = PlaySession.NewId() } : session);
            }

            if (valid.Count > 0)
            {
                kept[id] = valid;
            }
        }

        return new PlayLog(kept, warnings);
    }

    private sealed record Envelope(int Version, Dictionary<string, List<PlaySession>>? Games);
}
