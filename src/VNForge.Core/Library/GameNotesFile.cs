using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As notas dos jogos em <c>library-notes.json</c>, ao lado do <c>library.json</c>, na pasta de configuração do usuário. Fica à parte para reescrever a biblioteca
/// nunca tocar nas notas, e a nota de um jogo que saiu da lista volta se ele for achado de novo (o id vem da pasta). Como a biblioteca, um arquivo ilegível não
/// vira lista vazia: <see cref="Load"/> recusa em vez de deixar o próximo <see cref="Update{T}"/> sobrescrevê-lo.
/// </summary>
public sealed class GameNotesFile
{
    public const string FileName = "library-notes.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonStore<GameNotes> _store;

    public GameNotesFile(string directory)
    {
        Path = System.IO.Path.Combine(directory, FileName);
        _store = new JsonStore<GameNotes>(
            Path,
            "as notas",
            $"As notas ('{Path}') estão em uso por outro processo. Tente de novo em instantes.",
            () => new GameNotes(),
            Parse,
            Serialize);
    }

    public string Path { get; }

    /// <summary>As notas do arquivo; vazias quando o arquivo não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public GameNotes Load() => _store.Load();

    /// <summary>
    /// Lê as notas, aplica <paramref name="change"/> e grava, com o arquivo travado: quem mudou o arquivo no meio (outro <c>vnf</c>, o app) não perde o que gravou.
    /// Se <paramref name="change"/> lançar, nada é gravado.
    /// </summary>
    /// <exception cref="LibraryException">O arquivo está ilegível, não pôde ser gravado, ou outro processo o segurou por mais de <see cref="LibraryFile.LockTimeout"/>.</exception>
    public T Update<T>(Func<GameNotes, T> change) => _store.Update(change);

    /// <inheritdoc cref="Update{T}(Func{GameNotes, T})"/>
    public void Update(Action<GameNotes> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _store.Update(notes =>
        {
            change(notes);
            return 0;
        });
    }

    public void Save(GameNotes notes) => _store.Save(notes);

    /// <summary>O JSON das notas, com os jogos por id, para o arquivo mudar só quando as notas mudam.</summary>
    public static string Serialize(GameNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var games = new SortedDictionary<string, NoteEntry?>(StringComparer.Ordinal);
        foreach (var id in notes.GameIds)
        {
            var note = notes.Of(id)!;
            games[id] = new NoteEntry(note.Text, note.UpdatedAt);
        }

        return JsonSerializer.Serialize(new Envelope(CurrentVersion, games), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, jogo sem id, ou nota sem texto ou longa demais.</exception>
    public static GameNotes Parse(string json, string filePath = "(memória)")
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
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} das notas da biblioteca; esta versão do VNForge lê a {CurrentVersion}.");
        }

        var notes = new GameNotes();
        foreach (var (gameId, entry) in envelope.Games ?? [])
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                throw new LibraryException($"O arquivo '{filePath}': há uma nota de um jogo sem id.");
            }

            if (entry?.Text is not { } text || text.Length == 0)
            {
                throw new LibraryException($"O arquivo '{filePath}': a nota do jogo {gameId} está sem texto.");
            }

            if (text.Length > GameNotes.MaxLength)
            {
                throw new LibraryException($"O arquivo '{filePath}': a nota do jogo {gameId} passa de {GameNotes.MaxLength} caracteres.");
            }

            notes.Load(gameId, new GameNote(text, entry.UpdatedAt));
        }

        return notes;
    }

    private sealed record Envelope(int Version, SortedDictionary<string, NoteEntry?>? Games);

    private sealed record NoteEntry(string? Text, DateTimeOffset UpdatedAt);
}
