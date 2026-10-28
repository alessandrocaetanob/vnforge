using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ReviewDiaryTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // Domingo 25/10/2026, 15:30: a semana de hoje começa neste mesmo dia.
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static SessionsFile Sessions(params ApprovalRecord[] approvals) => new(SessionsFile.CurrentVersion, approvals);

    private static IEnumerable<ApprovalRecord> Day(DateTimeOffset at, int lines, string script = "yst00001", int firstUnit = 0) =>
        Enumerable.Range(0, lines).Select(i => new ApprovalRecord(script, $"{script}:{firstUnit + i:D6}", at.AddSeconds(i * 30)));

    [Fact]
    public void Sem_aprovacoes_o_diario_esta_vazio()
    {
        var diary = ReviewDiary.Compute(SessionsFile.Empty, Now, Utc);

        Assert.Empty(diary.Days);
        Assert.Equal(0, diary.Streak);
        Assert.Equal(0, diary.Total);
        Assert.False(diary.HasActivity);
    }

    [Fact]
    public void A_janela_vai_do_domingo_de_onze_semanas_atras_ate_hoje_e_o_primeiro_dia_e_sempre_domingo()
    {
        var diary = ReviewDiary.Compute(Sessions([.. Day(Now, 1)]), Now, Utc);

        Assert.Equal(new DateOnly(2026, 8, 9), diary.Days[0].Date);
        Assert.Equal(DayOfWeek.Sunday, diary.Days[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 25), diary.Days[^1].Date);
        Assert.Equal(11 * 7 + 1, diary.Days.Count);
    }

    [Fact]
    public void Numa_quarta_a_ultima_coluna_termina_na_quarta()
    {
        var wednesday = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

        var diary = ReviewDiary.Compute(Sessions([.. Day(wednesday, 1)]), wednesday, Utc);

        Assert.Equal(new DateOnly(2026, 8, 9), diary.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 28), diary.Days[^1].Date);
        Assert.Equal(11 * 7 + 4, diary.Days.Count);
    }

    [Fact]
    public void Cada_dia_conta_as_linhas_diferentes_e_uma_linha_aprovada_duas_vezes_no_dia_conta_uma()
    {
        var yesterday = Now.AddDays(-1);
        var sessions = Sessions([.. Day(yesterday, 3), new ApprovalRecord("yst00001", "yst00001:000000", yesterday.AddHours(2)), .. Day(Now, 2, firstUnit: 100)]);

        var diary = ReviewDiary.Compute(sessions, Now, Utc);

        Assert.Equal(3, diary.Yesterday);
        Assert.Equal(2, diary.Today);
        Assert.Equal(5, diary.Total);
        Assert.Equal(3, diary.Days.Single(d => d.Date == new DateOnly(2026, 10, 24)).Lines);
    }

    [Fact]
    public void O_nivel_da_celula_vai_de_zero_ao_maximo_da_janela()
    {
        var sessions = Sessions([.. Day(Now.AddDays(-3), 100), .. Day(Now.AddDays(-2), 50, firstUnit: 1_000), .. Day(Now.AddDays(-1), 1, firstUnit: 2_000)]);

        var diary = ReviewDiary.Compute(sessions, Now, Utc);

        int Level(int daysAgo) => diary.Days.Single(d => d.Date == new DateOnly(2026, 10, 25).AddDays(-daysAgo)).Level;
        Assert.Equal(4, Level(3));
        Assert.Equal(2, Level(2));
        Assert.Equal(1, Level(1));
        Assert.Equal(0, Level(4));
        Assert.All(diary.Days, d => Assert.Equal(d.Lines == 0, d.Level == 0));
    }

    [Fact]
    public void Os_dias_seguidos_contam_de_hoje_para_tras_e_param_no_primeiro_dia_vazio()
    {
        var sessions = Sessions([.. Day(Now, 1), .. Day(Now.AddDays(-1), 1, firstUnit: 10), .. Day(Now.AddDays(-2), 1, firstUnit: 20), .. Day(Now.AddDays(-4), 1, firstUnit: 40)]);

        Assert.Equal(3, ReviewDiary.Compute(sessions, Now, Utc).Streak);
    }

    [Fact]
    public void Hoje_sem_aprovacao_ainda_nao_quebra_a_sequencia_que_vale_ate_ontem()
    {
        var sessions = Sessions([.. Day(Now.AddDays(-1), 1), .. Day(Now.AddDays(-2), 1, firstUnit: 10)]);

        var diary = ReviewDiary.Compute(sessions, Now, Utc);

        Assert.Equal(2, diary.Streak);
        Assert.Equal(0, diary.Today);
        Assert.Equal(1, diary.Yesterday);
    }

    [Fact]
    public void Sem_aprovacao_ontem_nem_hoje_a_sequencia_zera()
    {
        var sessions = Sessions([.. Day(Now.AddDays(-2), 5)]);

        Assert.Equal(0, ReviewDiary.Compute(sessions, Now, Utc).Streak);
    }

    [Fact]
    public void A_sequencia_pode_passar_da_janela_do_mapa_ate_o_que_o_registro_guarda()
    {
        var sessions = Sessions([.. Enumerable.Range(0, 88).SelectMany(d => Day(Now.AddDays(-d), 1, firstUnit: d * 10))]);

        Assert.Equal(88, ReviewDiary.Compute(sessions, Now, Utc).Streak);
    }

    [Fact]
    public void O_dia_vale_pelo_fuso_da_pessoa()
    {
        var saoPaulo = TimeZoneInfo.CreateCustomTimeZone("brt", TimeSpan.FromHours(-3), "BRT", "BRT");
        // 01:00 UTC de domingo ainda é sábado à noite em Brasília; duas horas depois o dia virou para o domingo.
        var late = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero);

        var diary = ReviewDiary.Compute(Sessions([.. Day(late, 2)]), late.AddHours(4), saoPaulo);

        Assert.Equal(0, diary.Today);
        Assert.Equal(2, diary.Yesterday);
        Assert.Equal(2, ReviewDiary.Compute(Sessions([.. Day(late, 2)]), late.AddHours(1), saoPaulo).Today);
    }

    [Fact]
    public void Aprovacoes_mais_velhas_que_a_janela_nao_entram_no_mapa()
    {
        var sessions = Sessions([.. Day(Now.AddDays(-89), 7)]);

        var diary = ReviewDiary.Compute(sessions, Now, Utc);

        Assert.Equal(0, diary.Total);
        Assert.Empty(diary.Days);
    }

    [Fact]
    public void O_resumo_da_sessao_conta_as_linhas_desde_a_abertura_e_o_tempo_trabalhado()
    {
        var opened = Now.AddHours(-1);
        var sessions = Sessions([.. Day(Now.AddDays(-1), 50, firstUnit: 500), .. Day(opened.AddMinutes(5), 20)]);

        var summary = SessionSummary.Compute(sessions, opened, Now);

        Assert.Equal(20, summary.Lines);
        // 30 s da primeira linha mais 19 intervalos de 30 s.
        Assert.Equal(TimeSpan.FromSeconds(30 * 20), summary.Worked);
    }

    [Fact]
    public void Sem_aprovacao_na_sessao_o_resumo_e_vazio()
    {
        var summary = SessionSummary.Compute(Sessions([.. Day(Now.AddDays(-1), 5)]), Now.AddHours(-1), Now);

        Assert.Equal(SessionSummary.None, summary);
    }
}
