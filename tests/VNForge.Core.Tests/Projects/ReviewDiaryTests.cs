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
