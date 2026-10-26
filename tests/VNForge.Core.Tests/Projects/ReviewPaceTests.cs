using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ReviewPaceTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static SessionsFile Sessions(params ApprovalRecord[] approvals) => new(SessionsFile.CurrentVersion, approvals);

    private static ApprovalRecord Approval(int unit, DateTimeOffset at, string script = "yst00001") => new(script, $"{script}:{unit:D6}", at);

    private static IEnumerable<ApprovalRecord> Burst(int count, DateTimeOffset start, int firstUnit = 0) =>
        Enumerable.Range(0, count).Select(i => Approval(firstUnit + i, start.AddSeconds(i)));

    [Fact]
    public void Sem_aprovacoes_nao_ha_ritmo_nem_horas_que_faltam()
    {
        var pace = ReviewPace.Compute(SessionsFile.Empty, 1_000, Now, Utc);

        Assert.Equal(ReviewPace.None, pace);
    }

    [Fact]
    public void A_media_e_linhas_por_hora_trabalhada_e_as_horas_que_faltam_arredondam_para_cima()
    {
        // 120 linhas em uma hora hoje e 60 em outra ontem: 180 linhas em 2 horas = 90 por hora.
        var sessions = Sessions([.. Burst(120, Now.AddHours(-3)), .. Burst(60, Now.AddDays(-1), firstUnit: 1_000)]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 1_000, Now, Utc);

        Assert.Equal(120, pace.Today);
        Assert.Equal(90, pace.PerHour);
        Assert.Equal(12, pace.HoursLeft);
    }

    [Fact]
    public void Dias_parados_nao_baixam_a_media()
    {
        var parado = Sessions([.. Burst(100, Now.AddDays(-6))]);

        var pace = ReviewPace.Compute(parado, linesToReview: 300, Now, Utc);

        Assert.Equal(0, pace.Today);
        Assert.Equal(100, pace.PerHour);
        Assert.Equal(3, pace.HoursLeft);
    }

    [Fact]
    public void Aprovacoes_com_mais_de_sete_dias_ficam_de_fora_da_media()
    {
        var sessions = Sessions([.. Burst(500, Now.AddDays(-8)), .. Burst(50, Now.AddHours(-1), firstUnit: 1_000)]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 100, Now, Utc);

        Assert.Equal(50, pace.PerHour);
        Assert.Equal(2, pace.HoursLeft);
    }

    [Fact]
    public void Aprovar_a_mesma_linha_de_novo_conta_uma_vez()
    {
        var sessions = Sessions(Approval(1, Now.AddHours(-2)), Approval(1, Now.AddHours(-1)), Approval(2, Now.AddHours(-1)));

        var pace = ReviewPace.Compute(sessions, linesToReview: 20, Now, Utc);

        Assert.Equal(1, pace.PerHour);
        Assert.Equal(20, pace.HoursLeft);
        Assert.Equal(2, pace.Today);
    }

    [Fact]
    public void Nada_para_revisar_zera_as_horas_mas_mantem_o_ritmo()
    {
        var sessions = Sessions([.. Burst(10, Now.AddHours(-1))]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 0, Now, Utc);

        Assert.Equal(10, pace.PerHour);
        Assert.Equal(0, pace.HoursLeft);
    }

    [Fact]
    public void A_hora_e_a_do_relogio_no_fuso_da_pessoa()
    {
        // 23h30 e 00h10 (UTC) são a mesma hora no fuso de 30 minutos de diferença e duas horas no UTC.
        var zone = TimeZoneInfo.CreateCustomTimeZone("meia-hora", TimeSpan.FromMinutes(30), "meia-hora", "meia-hora");
        var sessions = Sessions(Approval(1, new DateTimeOffset(2026, 10, 24, 23, 40, 0, TimeSpan.Zero)), Approval(2, new DateTimeOffset(2026, 10, 25, 0, 10, 0, TimeSpan.Zero)));

        var emUtc = ReviewPace.Compute(sessions, 100, Now, Utc);
        var noFuso = ReviewPace.Compute(sessions, 100, Now, zone);

        Assert.Equal(1, emUtc.PerHour);
        Assert.Equal(2, noFuso.PerHour);
    }
}
