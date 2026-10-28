namespace VNForge.Core.Projects;

/// <summary>Um dia do diário: a data no fuso da pessoa, quantas linhas diferentes foram aprovadas nele e a intensidade (0 a 4) da célula do mapa de calor.</summary>
public sealed record DiaryDay(DateOnly Date, int Lines, int Level);

/// <summary>
/// O diário de tradução, lido de <c>.cache/sessions.json</c>: as aprovações por dia nas últimas <see cref="Weeks"/> semanas, os dias seguidos de trabalho e o que
/// foi aprovado hoje e ontem. Cada linha conta uma vez por dia, como em <see cref="SessionsFile.ReviewedOn"/>.
/// </summary>
/// <param name="Days">
/// Os dias da janela em ordem, do primeiro domingo ao dia de hoje; o dia <c>i</c> fica na coluna <c>i / 7</c> e na linha <c>i % 7</c> do mapa (domingo no alto).
/// Vazio quando o registro não tem nenhuma aprovação.
/// </param>
/// <param name="Today">Linhas aprovadas hoje.</param>
/// <param name="Yesterday">Linhas aprovadas ontem.</param>
/// <param name="Streak">Dias seguidos com pelo menos uma aprovação, contados de hoje para trás. Hoje sem aprovação ainda não quebra a sequência: ela vale até ontem.</param>
/// <param name="Total">Linhas aprovadas na janela.</param>
public sealed record ReviewDiary(IReadOnlyList<DiaryDay> Days, int Today, int Yesterday, int Streak, int Total)
{
    /// <summary>Quantas semanas o mapa mostra; o registro guarda 90 dias, o bastante para elas (no máximo 83 dias).</summary>
    public const int Weeks = 12;

    public static ReviewDiary Empty { get; } = new([], 0, 0, 0, 0);

    public bool HasActivity => Total > 0 || Streak > 0;

    public static ReviewDiary Compute(SessionsFile sessions, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(zone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var perDay = sessions.ReviewedPerDay(zone);
        var first = today.AddDays(-(int)today.DayOfWeek - (Weeks - 1) * 7);
        var counts = Enumerable.Range(0, today.DayNumber - first.DayNumber + 1)
            .Select(i => first.AddDays(i))
            .Select(date => (Date: date, Lines: perDay.GetValueOrDefault(date)))
            .ToList();
        var total = counts.Sum(c => c.Lines);
        var max = counts.Max(c => c.Lines);
        var days = counts.Select(c => new DiaryDay(c.Date, c.Lines, LevelOf(c.Lines, max))).ToList();

        var todayLines = perDay.GetValueOrDefault(today);
        return new ReviewDiary(total == 0 ? [] : days, todayLines, perDay.GetValueOrDefault(today.AddDays(-1)), StreakOf(perDay, today), total);
    }

    private static int LevelOf(int lines, int max) =>
        lines <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(4.0 * lines / max), 1, 4);

    private static int StreakOf(IReadOnlyDictionary<DateOnly, int> perDay, DateOnly today)
    {
        var day = perDay.GetValueOrDefault(today) > 0 ? today : today.AddDays(-1);
        var streak = 0;
        while (perDay.GetValueOrDefault(day) > 0)
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}

/// <summary>O que uma sessão do app produziu: as linhas aprovadas desde a abertura e o tempo trabalhado nelas.</summary>
/// <param name="Lines">Linhas diferentes aprovadas.</param>
/// <param name="Worked">O tempo trabalhado, pelo mesmo cálculo do ritmo de revisão (pausas longas não contam).</param>
public sealed record SessionSummary(int Lines, TimeSpan Worked)
{
    public static SessionSummary None { get; } = new(0, TimeSpan.Zero);

    /// <summary>O resumo das aprovações de <paramref name="since"/> até <paramref name="now"/>.</summary>
    public static SessionSummary Compute(SessionsFile sessions, DateTimeOffset since, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var times = sessions.Approvals
            .Where(a => a.At >= since && a.At <= now)
            .GroupBy(a => (a.ScriptKey, a.UnitId))
            .Select(g => g.Min(a => a.At))
            .Order()
            .ToList();
        return times.Count == 0 ? None : new SessionSummary(times.Count, ReviewPace.WorkedTime(times));
    }
}
