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
    public void A_media_e_linhas_por_hora_trabalhada_e_as_horas_que_faltam_arredondam_para_cima()
    {
        // 120 linhas em uma hora hoje e 60 em meia hora ontem: 180 linhas em 1,5 hora = 120 por hora.
        var sessions = Sessions([.. Burst(120, Now.AddHours(-3)), .. Burst(60, Now.AddDays(-1), firstUnit: 1_000)]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 1_000, Now, Utc);

        Assert.Equal(120, pace.Today);
        Assert.Equal(120, pace.PerHour);
        Assert.Equal(9, pace.HoursLeft);
    }

    [Fact]
    public void Meia_hora_de_trabalho_que_cruza_a_hora_do_relogio_conta_meia_hora()
    {
        var start = new DateTimeOffset(2026, 10, 25, 10, 45, 0, TimeSpan.Zero);

        var pace = ReviewPace.Compute(Sessions([.. Burst(60, start)]), linesToReview: 120, Now, Utc);

        Assert.Equal(120, pace.PerHour);
        Assert.Equal(1, pace.HoursLeft);
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
    public void Uma_aprovacao_so_vale_trinta_segundos_e_rajadas_valem_no_minimo_cinco_por_linha()
    {
        var single = ReviewPace.Compute(Sessions(Approval(1, Now.AddHours(-1))), linesToReview: 10, Now, Utc);
        var burst = ReviewPace.Compute(Sessions([.. Burst(120, Now.AddHours(-1), seconds: 1)]), linesToReview: 10, Now, Utc);

        Assert.Equal(120, single.PerHour);
        // 30 s da primeira linha mais 119 x 5 s: 120 linhas em 625 s.
        Assert.Equal(691, burst.PerHour);
    }

    [Fact]
    public void Dias_parados_nao_baixam_a_media()
    {
        var parado = Sessions([.. Burst(100, Now.AddDays(-6))]);

        var pace = ReviewPace.Compute(parado, linesToReview: 300, Now, Utc);

        Assert.Equal(0, pace.Today);
        Assert.Equal(120, pace.PerHour);
        Assert.Equal(3, pace.HoursLeft);
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
    public void Aprovar_a_mesma_linha_de_novo_conta_uma_vez()
    {
        var sessions = Sessions(Approval(1, Now.AddHours(-2)), Approval(1, Now.AddHours(-1)), Approval(2, Now.AddHours(-1)));

        var pace = ReviewPace.Compute(sessions, linesToReview: 20, Now, Utc);

        // Duas linhas, cada uma numa sessão de uma aprovação: 1 minuto no total.
        Assert.Equal(120, pace.PerHour);
        Assert.Equal(1, pace.HoursLeft);
        Assert.Equal(2, pace.Today);
    }

    [Fact]
    public void Nada_para_revisar_zera_as_horas_mas_mantem_o_ritmo()
    {
        var sessions = Sessions([.. Burst(10, Now.AddHours(-1))]);

        var pace = ReviewPace.Compute(sessions, linesToReview: 0, Now, Utc);

        Assert.Equal(120, pace.PerHour);
        Assert.Equal(0, pace.HoursLeft);
    }

    [Fact]
    public void O_hoje_segue_o_dia_no_fuso_da_pessoa()
    {
        // 23:40 de ontem e 00:10 de hoje em UTC são as duas de hoje num fuso com meia hora a mais.
        var zone = TimeZoneInfo.CreateCustomTimeZone("meia-hora", TimeSpan.FromMinutes(30), "meia-hora", "meia-hora");
        var sessions = Sessions(Approval(1, new DateTimeOffset(2026, 10, 24, 23, 40, 0, TimeSpan.Zero)), Approval(2, new DateTimeOffset(2026, 10, 25, 0, 10, 0, TimeSpan.Zero)));

        var moment = new DateTimeOffset(2026, 10, 25, 0, 20, 0, TimeSpan.Zero);

        Assert.Equal(1, ReviewPace.Compute(sessions, 100, moment, Utc).Today);
        Assert.Equal(2, ReviewPace.Compute(sessions, 100, moment, zone).Today);
    }
}
