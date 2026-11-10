using System.Globalization;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// Uma vez que a pessoa jogou. <paramref name="Duration"/> é o tempo contado (sem o tempo suspenso por mais que o limite nem o tempo em que o PC dormiu), então é
/// no máximo <c><paramref name="EndedAt"/> - <paramref name="StartedAt"/></c>; nunca é negativa e <paramref name="EndedAt"/> nunca é anterior a <paramref name="StartedAt"/>.
/// <paramref name="Open"/> marca a sessão em andamento: <paramref name="EndedAt"/> e <paramref name="Duration"/> valem até o último instante visto. Uma sessão que ficou aberta
/// sem ninguém atualizá-la (o app ou o terminal fechou no meio) vira <paramref name="Interrupted"/>, com o que foi contado até ali.
/// </summary>
public sealed record PlaySession(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Interrupted = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Open = false)
{
    /// <summary>Uma sessão de <paramref name="Id"/> novo.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Duration >= TimeSpan.Zero && EndedAt >= StartedAt;
}

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

    public PlayLog(IEnumerable<KeyValuePair<string, IReadOnlyList<PlaySession>>>? sessions = null, IReadOnlyList<string>? warnings = null)
    {
        _sessions = new Dictionary<string, List<PlaySession>>(StringComparer.Ordinal);
        foreach (var (id, list) in sessions ?? [])
        {
            _sessions[id] = [.. list];
        }

        Warnings = warnings ?? [];
    }

    /// <summary>O que a leitura do arquivo descartou (sessão com duração negativa ou que termina antes de começar), em português; vazio quando nada foi descartado.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<PlaySession>> All =>
        _sessions.ToDictionary(p => p.Key, p => (IReadOnlyList<PlaySession>)p.Value, StringComparer.Ordinal);

    public IReadOnlyList<PlaySession> SessionsOf(string gameId) =>
        _sessions.TryGetValue(gameId, out var list) ? list : [];

    /// <summary>Soma a sessão, ou troca a de mesmo <see cref="PlaySession.Id"/> (é assim que a sessão aberta anda e fecha).</summary>
    /// <exception cref="ArgumentException">A sessão tem duração negativa, termina antes de começar ou não tem id.</exception>
    public void Upsert(string gameId, PlaySession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsValid)
        {
            throw new ArgumentException("A sessão precisa de id, duração que não seja negativa e fim que não seja anterior ao começo.", nameof(session));
        }

        if (!_sessions.TryGetValue(gameId, out var list))
        {
            _sessions[gameId] = list = [];
        }

        var index = list.FindIndex(s => s.Id == session.Id);
        if (index < 0)
        {
            list.Add(session);
        }
        else
        {
            list[index] = session;
        }
    }

    /// <summary>
    /// Fecha como interrompida a sessão que ficou aberta e não foi atualizada nos últimos <paramref name="staleAfter"/>: o app ou o terminal fechou no meio.
    /// Guarda o que foi contado até o último instante visto. Devolve quantas fechou.
    /// </summary>
    public int CloseStale(DateTimeOffset now, TimeSpan staleAfter)
    {
        var closed = 0;
        foreach (var list in _sessions.Values)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Open && now - list[i].EndedAt > staleAfter)
                {
                    list[i] = list[i] with { Open = false, Interrupted = true };
                    closed++;
                }
            }
        }

        return closed;
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

    /// <summary>Quando foi o último jogo, pela data do PC: <c>Hoje, 21:14</c>, <c>Ontem, 20:02</c> ou <c>Há 5 dias</c>.</summary>
    public static string LastPlayed(DateTimeOffset last, DateTimeOffset now)
    {
        var (when, today) = (last.ToLocalTime(), now.ToLocalTime().Date);
        return (today - when.Date).Days switch
        {
            <= 0 => string.Create(CultureInfo.InvariantCulture, $"Hoje, {when:HH:mm}"),
            1 => string.Create(CultureInfo.InvariantCulture, $"Ontem, {when:HH:mm}"),
            var days => string.Create(CultureInfo.InvariantCulture, $"Há {days} dias"),
        };
    }

    /// <summary>O tempo de jogo de um jogo em uma linha: <c>27 h 40 min em 14 sessões · último jogado Hoje, 21:14</c>, ou <c>Ainda não jogado</c>.</summary>
    public static string Summary(PlayStats stats, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (stats.Sessions == 0)
        {
            return "Ainda não jogado";
        }

        var sessions = stats.Sessions == 1 ? "1 sessão" : string.Create(CultureInfo.InvariantCulture, $"{stats.Sessions} sessões");
        var text = $"{Format(stats.Total)} em {sessions}";
        return stats.LastPlayedAt is { } last ? $"{text} · último jogado {LastPlayed(last, now)}" : text;
    }
}
