namespace VNForge.Core.Library;

/// <summary>A nota livre de um jogo. <paramref name="Text"/> usa <c>\n</c> como quebra de linha e não termina em espaço.</summary>
public sealed record GameNote(string Text, DateTimeOffset UpdatedAt);

/// <summary>
/// As notas livres da pessoa sobre cada jogo da biblioteca, uma por jogo, por id. Em memória; <see cref="GameNotesFile"/> lê e grava.
/// Uma nota vazia não existe: gravar texto em branco apaga a nota.
/// </summary>
public sealed class GameNotes
{
    public const int MaxLength = 100_000;

    private readonly Dictionary<string, GameNote> _byGame = new(StringComparer.Ordinal);

    /// <summary>Os ids dos jogos que têm nota, por id.</summary>
    public IReadOnlyList<string> GameIds => [.. _byGame.Keys.Order(StringComparer.Ordinal)];

    /// <summary>A nota do jogo, ou nulo quando ele não tem.</summary>
    public GameNote? Of(string gameId) => _byGame.GetValueOrDefault(gameId);

    /// <summary>Troca a nota do jogo. Texto vazio ou só de espaços apaga a nota. Devolve a nota gravada, ou nulo quando ela foi apagada.</summary>
    /// <exception cref="LibraryException">O texto passa de <see cref="MaxLength"/> caracteres.</exception>
    public GameNote? Set(string gameId, string? text, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var clean = Clean(text);
        if (clean.Length == 0)
        {
            _byGame.Remove(gameId);
            return null;
        }

        var note = new GameNote(clean, now);
        _byGame[gameId] = note;
        return note;
    }

    /// <summary>Acrescenta o texto numa linha nova ao fim da nota (ou começa a nota).</summary>
    /// <exception cref="LibraryException">O texto está vazio, ou a nota passaria de <see cref="MaxLength"/> caracteres.</exception>
    public GameNote Append(string gameId, string? text, DateTimeOffset now)
    {
        var clean = Clean(text);
        if (clean.Length == 0)
        {
            throw new LibraryException("O texto a acrescentar na nota está vazio.");
        }

        var current = Of(gameId);
        return Set(gameId, current is null ? clean : current.Text + "\n" + clean, now)!;
    }

    /// <summary>Apaga a nota do jogo. Devolve se ele tinha nota.</summary>
    public bool Clear(string gameId) => _byGame.Remove(gameId);

    /// <summary>Quebras de linha viram <c>\n</c> e as pontas perdem linhas e espaços em branco; o recuo da primeira linha fica, porque pode ser de uma lista.</summary>
    private static string Clean(string? text)
    {
        var clean = (text ?? string.Empty).ReplaceLineEndings("\n").TrimEnd().TrimStart('\n');
        return clean.Length > MaxLength
            ? throw new LibraryException($"A nota passa de {MaxLength} caracteres.")
            : clean;
    }

    internal void Load(string gameId, GameNote note) => _byGame[gameId] = note;
}
