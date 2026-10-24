using VNForge.App.Services;

namespace VNForge.App.Tests;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 15, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "agora")]
    [InlineData(2, "há 2 min")]
    [InlineData(59, "há 59 min")]
    public void Minutos(int minutos, string esperado) =>
        Assert.Equal(esperado, RelativeTime.Format(Now.AddMinutes(-minutos), Now));

    [Fact]
    public void Horas_no_mesmo_dia() =>
        Assert.Equal("há 3 h", RelativeTime.Format(Now.AddHours(-3), Now));

    [Fact]
    public void Ontem_e_o_dia_anterior_mesmo_com_menos_de_24_horas() =>
        Assert.Equal("Ontem", RelativeTime.Format(new DateTimeOffset(2026, 10, 23, 23, 50, 0, TimeSpan.Zero), Now));

    [Theory]
    [InlineData(2, "há 2 dias")]
    [InlineData(7, "há 7 dias")]
    public void Dias_ate_sete(int dias, string esperado) =>
        Assert.Equal(esperado, RelativeTime.Format(Now.AddDays(-dias), Now));

    [Fact]
    public void Depois_de_sete_dias_mostra_a_data() =>
        Assert.Equal("12/10/2026", RelativeTime.Format(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), Now));

    [Fact]
    public void Data_no_futuro_vira_agora() =>
        Assert.Equal("agora", RelativeTime.Format(Now.AddMinutes(5), Now));
}
