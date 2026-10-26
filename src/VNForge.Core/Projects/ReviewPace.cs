namespace VNForge.Core.Projects;

/// <summary>
/// O ritmo de revisão da pessoa, lido de <c>.cache/sessions.json</c>: linhas aprovadas hoje, a média por hora trabalhada nos últimos sete dias
/// e as horas que faltam para revisar as linhas que a IA traduziu nesse ritmo.
/// </summary>
/// <param name="Today">Linhas diferentes aprovadas no dia de hoje, no fuso da pessoa.</param>
/// <param name="PerHour">Linhas por hora trabalhada nos últimos sete dias; nulo quando nenhuma linha foi aprovada nesse tempo.</param>
/// <param name="HoursLeft">Horas para revisar o que falta, no ritmo de <see cref="PerHour"/>, arredondadas para cima; nulo sem ritmo, zero sem linhas para revisar.</param>
public sealed record ReviewPace(int Today, int? PerHour, int? HoursLeft)
{
    /// <summary>A janela da média: os últimos sete dias.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>Uma pausa maior que isto entre duas aprovações encerra a sessão: o tempo parado não conta como trabalho.</summary>
    public static readonly TimeSpan IdleCutoff = TimeSpan.FromMinutes(10);

    /// <summary>O tempo que a primeira aprovação de uma sessão vale (a pessoa já estava lendo a linha), e que uma sessão de uma aprovação só conta.</summary>
    public static readonly TimeSpan FirstLineTime = TimeSpan.FromSeconds(30);

    /// <summary>O menos que uma aprovação conta: ninguém lê e aprova uma linha em menos que isto, e aprovações em rajada não inflam a média.</summary>
    public static readonly TimeSpan MinLineTime = TimeSpan.FromSeconds(5);

    public static ReviewPace None { get; } = new(0, null, null);

    /// <summary>
    /// Calcula o ritmo. O tempo trabalhado soma, para cada linha aprovada, o intervalo desde a aprovação anterior quando ele é de até
    /// <see cref="IdleCutoff"/> (nunca menos que <see cref="MinLineTime"/>); a primeira linha de cada sessão vale <see cref="FirstLineTime"/>.
    /// Uma linha aprovada duas vezes na janela conta uma, na primeira vez.
    /// </summary>
    /// <param name="linesToReview">Linhas que ainda esperam revisão (a IA ou o tradutor as deixaram).</param>
    public static ReviewPace Compute(SessionsFile sessions, int linesToReview, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(zone);
        var today = sessions.ReviewedOn(now, zone);
        var since = now - Window;
        var times = sessions.Approvals
            .Where(a => a.At > since && a.At <= now)
            .GroupBy(a => (a.ScriptKey, a.UnitId))
            .Select(g => g.Min(a => a.At))
            .Order()
            .ToList();
        if (times.Count == 0)
        {
            return new ReviewPace(today, null, null);
        }

        var worked = WorkedTime(times);
        var perHour = Math.Max(1, (int)Math.Round(times.Count / worked.TotalHours, MidpointRounding.AwayFromZero));
        var left = linesToReview <= 0 ? 0 : (int)Math.Ceiling((double)linesToReview / perHour);
        return new ReviewPace(today, perHour, left);
    }

    private static TimeSpan WorkedTime(IReadOnlyList<DateTimeOffset> times)
    {
        var worked = FirstLineTime;
        for (var i = 1; i < times.Count; i++)
        {
            var gap = times[i] - times[i - 1];
            worked += gap > IdleCutoff ? FirstLineTime : TimeSpan.FromTicks(Math.Max(gap.Ticks, MinLineTime.Ticks));
        }

        return worked;
    }
}
