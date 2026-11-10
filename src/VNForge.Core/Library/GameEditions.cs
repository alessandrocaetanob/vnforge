namespace VNForge.Core.Library;

/// <summary>
/// As edições do VNDB (<c>r123</c>) que casam com a pasta de um jogo da biblioteca. <paramref name="VisualNovelId"/> é o jogo do VNDB em que o casamento foi feito: se a
/// pessoa confirmar outro jogo para a mesma pasta, o registro deixa de valer. <paramref name="ReleaseIds"/> vazio é uma resposta (nenhuma edição se parece com a pasta),
/// que impede de perguntar ao VNDB de novo a cada vez.
/// </summary>
public sealed record GameEdition(string VisualNovelId, IReadOnlyList<string> ReleaseIds, DateTimeOffset MatchedAt);

/// <summary>
/// Quais edições do VNDB cada jogo da biblioteca é, por id do jogo. O papel e o spoiler de um personagem variam por edição, e a página de personagens usa a edição
/// casada com a pasta. Em memória; <see cref="GameEditionsFile"/> lê e grava.
/// </summary>
public sealed class GameEditions
{
    private readonly Dictionary<string, GameEdition> _byGame = new(StringComparer.Ordinal);

    /// <summary>Os ids dos jogos que têm registro, em ordem.</summary>
    public IReadOnlyList<string> GameIds => [.. _byGame.Keys.Order(StringComparer.Ordinal)];

    /// <summary>O registro do jogo, ou nulo quando ainda não se casou a edição.</summary>
    public GameEdition? Of(string gameId) => _byGame.GetValueOrDefault(gameId);

    /// <summary>As edições casadas com o jogo, se o registro é do jogo do VNDB confirmado agora; um registro de outro jogo (a confirmação mudou) não vale e dá lista vazia.</summary>
    public IReadOnlyList<string> CurrentFor(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return IsCurrent(game) ? Of(game.Id)!.ReleaseIds : [];
    }

    /// <summary>Se o casamento do jogo está feito para o jogo do VNDB confirmado agora.</summary>
    public bool IsCurrent(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.VndbId is not null && Of(game.Id)?.VisualNovelId == game.VndbId;
    }

    public GameEdition Set(string gameId, string visualNovelId, IEnumerable<string> releaseIds, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(visualNovelId);
        ArgumentNullException.ThrowIfNull(releaseIds);
        var edition = new GameEdition(visualNovelId, [.. releaseIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], now);
        _byGame[gameId] = edition;
        return edition;
    }

    /// <summary>Apaga o registro do jogo. Devolve se ele tinha um.</summary>
    public bool Clear(string gameId) => _byGame.Remove(gameId);

    internal void Load(string gameId, GameEdition edition) => _byGame[gameId] = edition;
}
