using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ReviewPaceTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static SessionsFile Sessions(params ApprovalRecord[] approvals) => new(SessionsFile.CurrentVersion, approvals);

    private static ApprovalRecord Approval(int unit, DateTimeOffset at, string script = "yst00001") => new(script, $"{script}:{unit:D6}", at);

    /// <summary>Linhas aprovadas uma a cada <paramref name="seconds"/> segundos; 30 s por linha são 120 linhas por hora.</summary>
    private static IEnumerable<ApprovalRecord> Burst(int count, DateTimeOffset start, int firstUnit = 0, int seconds = 30) =>
        Enumerable.Range(0, count).Select(i => Approval(firstUnit + i, start.AddSeconds(i * seconds)));

    [Fact]
    public void Sem_aprovacoes_nao_ha_ritmo_nem_horas_que_faltam()
    {
        var pace = ReviewPace.Compute(SessionsFile.Empty, 1_000, Now, Utc);

        Assert.Equal(ReviewPace.None, pace);
    }

    [Fact]
    public void Pausa_maior_que_dez_minutos_nao_conta_como_trabalho()
    {
        var morning = Burst(60, Now.AddHours(-6));
        var afternoon = Burst(60, Now.AddHours(-2), firstUnit: 1_000);

        var pace = ReviewPace.Compute(Sessions([.. morning, .. afternoon]), linesToReview: 240, Now, Utc);

        Assert.Equal(120, pace.PerHour);
        Assert.Equal(2, pace.HoursLeft);
    }

    [Fact]
    public void Aprovacoes_com_mais_de_sete_dias_ficam_de_fora_da_media()
    {
        var sessions = Sessions([.. Burst(500, Now.AddDays(-8), seconds: 5), .. Burst(50, Now.AddHours(-1), firstUnit: 1_000)]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 100, Now, Utc);

        Assert.Equal(120, pace.PerHour);
        Assert.Equal(1, pace.HoursLeft);
    }

    [Fact]
    public void Nada_para_revisar_zera_as_horas_mas_mantem_o_ritmo()
    {
        var sessions = Sessions([.. Burst(10, Now.AddHours(-1))]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 0, Now, Utc);

        Assert.Equal(120, pace.PerHour);
        Assert.Equal(0, pace.HoursLeft);
    }
}
