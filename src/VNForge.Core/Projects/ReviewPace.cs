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

    public static ReviewPace None { get; } = new(0, null, null);

    /// <summary>
    /// Calcula o ritmo. Uma hora trabalhada é uma hora do relógio (no fuso <paramref name="zone"/>) em que a pessoa aprovou ao menos uma linha, então
    /// os dias parados não puxam a média para baixo. Uma linha aprovada duas vezes na janela conta uma.
    /// </summary>
    /// <param name="linesToReview">Linhas que ainda esperam revisão (a IA ou o tradutor as deixaram).</param>
    public static ReviewPace Compute(SessionsFile sessions, int linesToReview, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(zone);
        var today = sessions.ReviewedOn(now, zone);
        var since = now - Window;
        var recent = sessions.Approvals.Where(a => a.At > since && a.At <= now).ToList();
        if (recent.Count == 0)
        {
            return new ReviewPace(today, null, null);
        }

        var lines = recent.Select(a => (a.ScriptKey, a.UnitId)).Distinct().Count();
        var hours = recent
            .Select(a => TimeZoneInfo.ConvertTime(a.At, zone))
            .Select(at => (at.Year, at.DayOfYear, at.Hour))
            .Distinct()
            .Count();
        var perHour = Math.Max(1, (int)Math.Round((double)lines / hours, MidpointRounding.AwayFromZero));
        var left = linesToReview <= 0 ? 0 : (int)Math.Ceiling((double)linesToReview / perHour);
        return new ReviewPace(today, perHour, left);
    }
}
