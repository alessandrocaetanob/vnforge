using System.Globalization;

namespace VNForge.Core.Library;

/// <summary>Uma linha da lista de sessões da aba Jogar: <c>Hoje, 21:14</c>, <c>1 h 52 min</c>, <c>Locale Emulator</c>.</summary>
public sealed record PlaySessionRow(string When, string Duration, string Via);

/// <summary>Uma semana do gráfico: a segunda-feira em que ela começa, o tempo jogado e quantas sessões começaram nela.</summary>
public sealed record PlayWeek(DateTime Start, TimeSpan Total, int Sessions);

/// <summary>O que a aba Jogar mostra do tempo de jogo de um jogo: a lista de sessões e o gráfico por semana.</summary>
public static class PlayHistory
{
    public const int Weeks = 12;

    /// <summary>As sessões da mais recente para a mais antiga. A sessão aberta (em andamento) entra com o tempo gravado até agora, como o quadro mostra ao lado de "Jogando agora".</summary>
    public static IReadOnlyList<PlaySessionRow> Rows(IEnumerable<PlaySession> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return
        [
            .. sessions
                .OrderByDescending(s => s.StartedAt)
                .Select(s => new PlaySessionRow(When(s.StartedAt, now), PlayTime.Format(s.Duration), s.ViaLocaleEmulator ? "Locale Emulator" : "Direto")),
        ];
    }

    /// <summary>O tempo e as sessões de cada uma das últimas <see cref="Weeks"/> semanas (de segunda a domingo, pela data do PC), da mais antiga à de agora.</summary>
    public static IReadOnlyList<PlayWeek> PerWeek(IEnumerable<PlaySession> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var thisWeek = WeekStart(now.ToLocalTime().Date);
        var totals = new TimeSpan[Weeks];
        var counts = new int[Weeks];
        foreach (var session in sessions)
        {
            var ago = (thisWeek - WeekStart(session.StartedAt.ToLocalTime().Date)).Days / 7;
            if (ago is >= 0 and < Weeks)
            {
                totals[Weeks - 1 - ago] += session.Duration;
                counts[Weeks - 1 - ago]++;
            }
        }

        return [.. totals.Select((total, i) => new PlayWeek(thisWeek.AddDays(7 * (i + 1 - Weeks)), total, counts[i]))];
    }

    /// <summary>As semanas que tiveram jogo, da mais recente para a mais antiga: <c>Esta semana</c>, <c>Semana passada</c> ou <c>Semana de 03/11</c>.</summary>
    public static IReadOnlyList<PlaySessionRow> WeekRows(IEnumerable<PlaySession> sessions, DateTimeOffset now)
    {
        var weeks = PerWeek(sessions, now);
        return
        [
            .. weeks
                .Select((week, i) => (Week: week, Ago: weeks.Count - 1 - i))
                .Where(p => p.Week.Sessions > 0)
                .Reverse()
                .Select(p => new PlaySessionRow(
                    p.Ago switch
                    {
                        0 => "Esta semana",
                        1 => "Semana passada",
                        _ => string.Create(CultureInfo.InvariantCulture, $"Semana de {p.Week.Start:dd/MM}"),
                    },
                    PlayTime.Format(p.Week.Total),
                    p.Week.Sessions == 1 ? "1 sessão" : string.Create(CultureInfo.InvariantCulture, $"{p.Week.Sessions} sessões"))),
        ];
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
