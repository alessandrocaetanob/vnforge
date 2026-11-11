using VNForge.App.Services;

namespace VNForge.App.Tests;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Horas_no_mesmo_dia() =>
        Assert.Equal("há 3 h", RelativeTime.Format(Now.AddHours(-3), Now));

    [Fact]
    public void Ontem_e_o_dia_anterior_mesmo_com_menos_de_24_horas() =>
        Assert.Equal("Ontem", RelativeTime.Format(new DateTimeOffset(2026, 10, 23, 23, 50, 0, TimeSpan.Zero), Now));

    [Fact]
    public void Depois_de_sete_dias_mostra_a_data() =>
        Assert.Equal("12/10/2026", RelativeTime.Format(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), Now));
}
