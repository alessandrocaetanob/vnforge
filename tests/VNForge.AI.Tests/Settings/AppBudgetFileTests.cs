using VNForge.AI.Settings;

namespace VNForge.AI.Tests.Settings;

public sealed class AppBudgetFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public AppBudgetFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string json) => File.WriteAllText(Path.Combine(_dir, AppBudgetFile.FileName), json);

    [Fact]
    public void Arquivo_ausente_vale_sem_limite_e_sem_problema()
    {
        var reading = AppBudgetFile.Load(_dir);

        Assert.Null(reading.Problem);
        Assert.Null(reading.Settings.Limits.Limit);
        Assert.False(reading.Settings.Rate.IsInformed);
    }

    [Fact]
    public void Arquivo_valido_traz_limite_aviso_e_cotacao()
    {
        Write("""{ "exchangeRate": 5.5, "exchangeRateOn": "2026-10-25", "spendLimit": 50, "warnAbove": 5 }""");

        var reading = AppBudgetFile.Load(_dir);

        Assert.Null(reading.Problem);
        Assert.Equal(50m, reading.Settings.Limits.Limit);
        Assert.Equal(5m, reading.Settings.Limits.WarnAbove);
        Assert.Equal(5.5m, reading.Settings.Rate.BrlPerUsd);
    }

    [Theory]
    [InlineData("""{ "spendLimit": "muito" }""", "spendLimit")]
    [InlineData("""{ "spendLimit": -5 }""", "spendLimit")]
    [InlineData("""{ "spendLimit": 1e40 }""", "spendLimit")]
    public void Valor_de_dinheiro_invalido_vira_problema(string content, string field)
    {
        Write(content);

        var reading = AppBudgetFile.Load(_dir);

        Assert.NotNull(reading.Problem);
        Assert.Contains(field, reading.Problem);
    }

    [Fact]
    public void Aviso_ou_cotacao_invalidos_com_limite_valido_nao_bloqueiam()
    {
        Write("""{ "spendLimit": 100, "warnAbove": "x", "exchangeRate": 500 }""");

        var reading = AppBudgetFile.Load(_dir);

        Assert.Null(reading.Problem);
        Assert.Equal(100m, reading.Settings.Limits.Limit);
    }

    [Fact]
    public void Campo_nulo_ou_ausente_nao_e_problema()
    {
        Write("""{ "spendLimit": null, "concurrency": 3 }""");

        Assert.Null(AppBudgetFile.Load(_dir).Problem);
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("[1, 2]")]
    public void Arquivo_ilegivel_diz_o_motivo_em_vez_de_valer_como_sem_limite(string content)
    {
        Write(content);

        var reading = AppBudgetFile.Load(_dir);

        Assert.NotNull(reading.Problem);
        Assert.Contains(AppBudgetFile.FileName, reading.Problem);
        Assert.Null(reading.Settings.Limits.Limit);
    }
}
