using System.Globalization;

namespace VNForge.Core.Library;

/// <summary>Uma linha da lista de sessões da aba Jogar: <c>Hoje, 21:14</c>, <c>1 h 52 min</c>, <c>Locale Emulator</c>.</summary>
public sealed record PlaySessionRow(string When, string Duration, string Via);

/// <summary>O que a aba Jogar mostra do tempo de jogo de um jogo: a lista de sessões e o gráfico por semana.</summary>
public static class PlayHistory
{
    public const int Weeks = 12;

    /// <summary>As sessões da mais recente para a mais antiga. A sessão aberta (em andamento) fica de fora: ela aparece em "Jogando agora".</summary>
    public static IReadOnlyList<PlaySessionRow> Rows(IEnumerable<PlaySession> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return
        [
            .. sessions
                .Where(s => !s.Open)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => new PlaySessionRow(When(s.StartedAt, now), PlayTime.Format(s.Duration), s.ViaLocaleEmulator ? "Locale Emulator" : "Direto")),
        ];
    }

    /// <summary>O tempo jogado em cada uma das últimas <see cref="Weeks"/> semanas (de segunda a domingo, pela data do PC), da mais antiga à de agora.</summary>
    public static IReadOnlyList<TimeSpan> PerWeek(IEnumerable<PlaySession> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var thisWeek = WeekStart(now.ToLocalTime().Date);
        var totals = new TimeSpan[Weeks];
        foreach (var session in sessions)
        {
            var ago = (thisWeek - WeekStart(session.StartedAt.ToLocalTime().Date)).Days / 7;
            if (ago is >= 0 and < Weeks)
            {
                totals[Weeks - 1 - ago] += session.Duration;
            }
        }

        return totals;
    }

    /// <summary>A hora de começo: <c>Hoje, 21:14</c>, <c>Ontem, 20:02</c> ou <c>06/11, 22:40</c>.</summary>
    public static string When(DateTimeOffset startedAt, DateTimeOffset now)
    {
        var when = startedAt.ToLocalTime();
        return (now.ToLocalTime().Date - when.Date).Days switch
        {
            <= 0 => string.Create(CultureInfo.InvariantCulture, $"Hoje, {when:HH:mm}"),
            1 => string.Create(CultureInfo.InvariantCulture, $"Ontem, {when:HH:mm}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{when:dd/MM, HH:mm}"),
        };
    }

    private static DateTime WeekStart(DateTime day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
