using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// A biblioteca em <c>library.json</c>, na pasta de configuração do usuário (a mesma do <c>recent.json</c>), fora de qualquer projeto.
/// Diferente dos recentes, um arquivo ilegível não vira lista vazia: ele guarda os casamentos que a pessoa confirmou, então <see cref="Load"/> recusa
/// em vez de deixar o próximo <see cref="Save"/> sobrescrevê-lo.
/// </summary>
public sealed class LibraryFile(string directory)
{
    public const string FileName = "library.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>A biblioteca do arquivo; vazia quando o arquivo não existe.</summary>
    /// <exception cref="LibraryException">O arquivo existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public GameLibrary Load()
    {
        if (!File.Exists(Path))
        {
            return new GameLibrary();
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

    /// <summary>Grava a biblioteca sem deixar o arquivo pela metade: escreve num temporário ao lado e troca.</summary>
    public void Save(GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, Serialize(library));
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static string Serialize(GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return JsonSerializer.Serialize(new Envelope(CurrentVersion, library.Privacy, [.. library.Games]), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, jogo sem id ou pasta, ou o mesmo id duas vezes.</exception>
    public static GameLibrary Parse(string json, string filePath = "(memória)")
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
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} da biblioteca; esta versão do VNForge lê a {CurrentVersion}.");
        }

        var games = envelope.Games ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            if (game is null || string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.Folder) || game.Engine is null || game.Title is null)
            {
                throw new LibraryException($"O arquivo '{filePath}': o jogo {i + 1} não tem id, pasta, engine ou título.");
            }

            if (!seen.Add(game.Id))
            {
                throw new LibraryException($"O arquivo '{filePath}': o id {game.Id} aparece mais de uma vez.");
            }
        }

        return new GameLibrary(games.Select(Normalize), envelope.Privacy ?? new LibraryPrivacy());
    }

    private static LibraryGame Normalize(LibraryGame game) => game with
    {
        Candidates = game.Candidates ?? [],
    };

    private sealed record Envelope(int Version, LibraryPrivacy? Privacy, List<LibraryGame>? Games);
}
