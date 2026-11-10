using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class PlayHistoryTests
{
    // Terça-feira; as horas são do PC, como a aba mostra.
    private static readonly DateTimeOffset Now = Local(2026, 11, 10, 22, 0);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

    private static PlaySession Session(DateTimeOffset started, int minutes, bool viaLocaleEmulator = false, bool open = false) =>
        new(Guid.NewGuid().ToString("N"), started, started.AddMinutes(minutes), TimeSpan.FromMinutes(minutes), Open: open, ViaLocaleEmulator: viaLocaleEmulator);

    [Fact]
    public void Lista_as_sessoes_da_mais_recente_para_a_mais_antiga_sem_a_que_esta_aberta()
    {
        var rows = PlayHistory.Rows(
            [
                Session(Local(2026, 11, 6, 22, 40), 48),
                Session(Local(2026, 11, 10, 21, 14), 112, viaLocaleEmulator: true),
                Session(Local(2026, 11, 9, 20, 2), 130),
                Session(Local(2026, 11, 10, 21, 50), 5, open: true),
            ],
            Now);

        Assert.Equal(
            [
                new PlaySessionRow("Hoje, 21:14", "1 h 52 min", "Locale Emulator"),
                new PlaySessionRow("Ontem, 20:02", "2 h 10 min", "Direto"),
                new PlaySessionRow("06/11, 22:40", "48 min", "Direto"),
            ],
            rows);
    }

    [Fact]
    public void Soma_o_tempo_por_semana_de_segunda_a_domingo_e_ignora_o_que_passa_de_doze_semanas()
    {
        var weeks = PlayHistory.PerWeek(
            [
                Session(Local(2026, 11, 9, 8, 0), 30),
                Session(Local(2026, 11, 10, 21, 14), 60),
                Session(Local(2026, 11, 8, 23, 0), 45),
                Session(Local(2026, 8, 24, 10, 0), 20),
                Session(Local(2026, 8, 23, 10, 0), 99),
            ],
            Now);

        Assert.Equal(PlayHistory.Weeks, weeks.Count);
        Assert.Equal(TimeSpan.FromMinutes(90), weeks[^1]);
        Assert.Equal(TimeSpan.FromMinutes(45), weeks[^2]);
        Assert.Equal(TimeSpan.FromMinutes(20), weeks[0]);
        Assert.Equal(TimeSpan.FromMinutes(155), weeks.Aggregate(TimeSpan.Zero, (sum, w) => sum + w));
    }
}
