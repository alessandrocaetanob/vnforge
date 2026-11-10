using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As edições casadas com cada jogo em <c>library-editions.json</c>, ao lado do <c>library.json</c>, na pasta de configuração do usuário. Fica à parte, como as notas e as
/// tags, para que reescrever a biblioteca nunca toque nele. Um arquivo ilegível não vira lista vazia: <see cref="Load"/> recusa.
/// </summary>
public sealed class GameEditionsFile
{
    public const string FileName = "library-editions.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonStore<GameEditions> _store;

    public GameEditionsFile(string directory)
    {
        Path = System.IO.Path.Combine(directory, FileName);
        _store = new JsonStore<GameEditions>(
            Path,
            "as edições dos jogos",
            $"As edições dos jogos ('{Path}') estão em uso por outro processo. Tente de novo em instantes.",
            () => new GameEditions(),
            Parse,
            Serialize);
    }

    public string Path { get; }

    /// <summary>As edições do arquivo; vazias quando ele não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public GameEditions Load() => _store.Load();

    /// <summary>Lê, aplica <paramref name="change"/> e grava com o arquivo travado: quem mudou o arquivo no meio (outro <c>vnf</c>, o app) não perde o que gravou.</summary>
    /// <exception cref="LibraryException">O arquivo está ilegível, não pôde ser gravado, ou outro processo o segurou por mais de <see cref="LibraryFile.LockTimeout"/>.</exception>
    public T Update<T>(Func<GameEditions, T> change) => _store.Update(change);

    /// <inheritdoc cref="Update{T}(Func{GameEditions, T})"/>
    public void Update(Action<GameEditions> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _store.Update(editions =>
        {
            change(editions);
            return 0;
        });
    }

    public void Save(GameEditions editions) => _store.Save(editions);

    public static string Serialize(GameEditions editions)
    {
        ArgumentNullException.ThrowIfNull(editions);
        var games = new SortedDictionary<string, EditionEntry?>(StringComparer.Ordinal);
        foreach (var id in editions.GameIds)
        {
            var edition = editions.Of(id)!;
            games[id] = new EditionEntry(edition.VisualNovelId, [.. edition.ReleaseIds], edition.MatchedAt, edition.Incomplete ? true : null);
        }

        return JsonSerializer.Serialize(new Envelope(CurrentVersion, games), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, jogo sem id, ou registro sem o jogo do VNDB.</exception>
    public static GameEditions Parse(string json, string filePath = "(memória)")
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
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} das edições da biblioteca; esta versão do VNForge lê a {CurrentVersion}.");
        }

        var editions = new GameEditions();
        foreach (var (gameId, entry) in envelope.Games ?? [])
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                throw new LibraryException($"O arquivo '{filePath}': há um registro de edição de um jogo sem id.");
            }

            if (string.IsNullOrWhiteSpace(entry?.VisualNovelId) || entry.ReleaseIds is null || entry.ReleaseIds.Exists(string.IsNullOrWhiteSpace))
            {
                throw new LibraryException($"O arquivo '{filePath}': o registro de edição do jogo {gameId} está sem o jogo do VNDB ou com uma edição sem id.");
            }

            editions.Load(gameId, new GameEdition(entry.VisualNovelId, [.. entry.ReleaseIds], entry.MatchedAt, entry.Incomplete == true));
        }

        return editions;
    }

    private sealed record Envelope(int Version, SortedDictionary<string, EditionEntry?>? Games);

    private sealed record EditionEntry(string? VisualNovelId, List<string>? ReleaseIds, DateTimeOffset MatchedAt, bool? Incomplete = null);
}
