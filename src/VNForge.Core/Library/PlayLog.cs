using System.Globalization;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// Uma vez que a pessoa jogou. <paramref name="Duration"/> é o tempo contado (sem o tempo em que o processo ficou suspenso por mais que o limite), então pode ser menor
/// que <c><paramref name="EndedAt"/> - <paramref name="StartedAt"/></c>. <paramref name="Interrupted"/> marca a sessão em que o VNForge parou de contar com o jogo ainda aberto.
/// </summary>
public sealed record PlaySession(DateTimeOffset StartedAt, DateTimeOffset EndedAt, TimeSpan Duration, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Interrupted = false);

/// <summary>O resumo do tempo de jogo de um jogo da biblioteca. <paramref name="LastPlayedAt"/> é o fim da última sessão; <see langword="null"/> se nunca jogou.</summary>
public sealed record PlayStats(TimeSpan Total, DateTimeOffset? LastPlayedAt, int Sessions)
{
    public static PlayStats None { get; } = new(TimeSpan.Zero, null, 0);
}

/// <summary>
/// As sessões de jogo por jogo da biblioteca (o id de <see cref="LibraryGame.Id"/>). Em memória; <see cref="PlayLogFile"/> lê e grava <c>playtime.json</c>.
/// Fica separado da <see cref="GameLibrary"/> para que jogar não regrave a biblioteca inteira.
/// </summary>
public sealed class PlayLog
{
    private readonly Dictionary<string, List<PlaySession>> _sessions;

    public PlayLog(IEnumerable<KeyValuePair<string, IReadOnlyList<PlaySession>>>? sessions = null)
    {
        _sessions = new Dictionary<string, List<PlaySession>>(StringComparer.Ordinal);
        foreach (var (id, list) in sessions ?? [])
        {
            _sessions[id] = [.. list];
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<PlaySession>> All =>
        _sessions.ToDictionary(p => p.Key, p => (IReadOnlyList<PlaySession>)p.Value, StringComparer.Ordinal);

    public IReadOnlyList<PlaySession> SessionsOf(string gameId) =>
        _sessions.TryGetValue(gameId, out var list) ? list : [];

    public void Add(string gameId, PlaySession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessions.TryGetValue(gameId, out var list))
        {
            _sessions[gameId] = list = [];
        }

        list.Add(session);
    }

    public PlayStats StatsOf(string gameId)
    {
        var sessions = SessionsOf(gameId);
        if (sessions.Count == 0)
        {
            return PlayStats.None;
        }

        return new PlayStats(
            TimeSpan.FromTicks(sessions.Sum(s => s.Duration.Ticks)),
            sessions.Max(s => s.EndedAt),
            sessions.Count);
    }

    /// <summary>
    /// Os jogos com o último jogado no topo; quem nunca foi jogado vem depois, por título. É a ordem da Biblioteca, que a tela e a CLI (<c>--recent</c>)
    /// compartilham.
    /// </summary>
    public IReadOnlyList<LibraryGame> ByLastPlayed(IEnumerable<LibraryGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        var title = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
        return
        [
            .. games
                .Select(g => (Game: g, Last: StatsOf(g.Id).LastPlayedAt))
                .OrderByDescending(p => p.Last is not null)
                .ThenByDescending(p => p.Last)
                .ThenBy(p => p.Game.Title, title)
                .ThenBy(p => p.Game.Folder, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Game),
        ];
    }
}

/// <summary>Como o tempo aparece em texto, em português: <c>menos de 1 min</c>, <c>35 min</c>, <c>2 h 05 min</c>.</summary>
public static class PlayTime
{
    public static string Format(TimeSpan time)
    {
        var minutes = (long)Math.Floor(time.TotalMinutes);
        if (minutes < 1)
        {
            return "menos de 1 min";
        }

        return minutes < 60
            ? $"{minutes} min"
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
