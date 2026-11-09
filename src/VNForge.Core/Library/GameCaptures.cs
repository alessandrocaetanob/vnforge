namespace VNForge.Core.Library;

/// <summary>
/// Uma captura da tela de um jogo. <paramref name="File"/> é só o nome do arquivo PNG (<c>aaaammdd-hhmmss.png</c>, na hora local da captura), dentro da pasta do jogo
/// em <see cref="GameCapturesFile.Directory"/>; <paramref name="CapturedAt"/> é o instante, com o fuso de quem capturou.
/// </summary>
public sealed record GameCapture(string File, DateTimeOffset CapturedAt, int Width, int Height, string? Note = null);

/// <summary>O índice das capturas de cada jogo da biblioteca, por id. Em memória; <see cref="GameCapturesFile"/> lê e grava o índice e as imagens.</summary>
public sealed class GameCaptures
{
    private readonly Dictionary<string, List<GameCapture>> _byGame = new(StringComparer.Ordinal);

    /// <summary>Os ids dos jogos que têm captura, por id.</summary>
    public IReadOnlyList<string> GameIds => [.. _byGame.Keys.Order(StringComparer.Ordinal)];

    /// <summary>As capturas do jogo, da mais antiga para a mais nova; as do mesmo instante ficam na ordem em que entraram (a ordenação é estável).</summary>
    public IReadOnlyList<GameCapture> Of(string gameId) =>
        _byGame.TryGetValue(gameId, out var list) ? [.. list.OrderBy(c => c.CapturedAt)] : [];

    public bool Contains(string gameId, string file) =>
        _byGame.TryGetValue(gameId, out var list) && list.Any(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));

    /// <exception cref="LibraryException">O jogo já tem uma captura com este arquivo, ou o nome do arquivo não é válido.</exception>
    public void Add(string gameId, GameCapture capture)
    {
        GameCapturesFile.ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(capture);
        GameCapturesFile.ValidateFileName(capture.File);
        if (Contains(gameId, capture.File))
        {
            throw new LibraryException($"O jogo {gameId} já tem uma captura com o arquivo '{capture.File}'.");
        }

        if (!_byGame.TryGetValue(gameId, out var list))
        {
            list = [];
            _byGame[gameId] = list;
        }

        list.Add(capture);
    }

    /// <summary>Tira a captura do índice. Devolve a que saiu, ou nulo quando o jogo não tinha esse arquivo.</summary>
    public GameCapture? Remove(string gameId, string file)
    {
        if (!_byGame.TryGetValue(gameId, out var list))
        {
            return null;
        }

        var found = list.Find(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return null;
        }

        list.Remove(found);
        if (list.Count == 0)
        {
            _byGame.Remove(gameId);
        }

        return found;
    }
}
