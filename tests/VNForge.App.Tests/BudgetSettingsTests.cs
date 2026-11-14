using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;

namespace VNForge.App.Tests;

[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
public class BudgetAppSettingsFileTests
{
    [Fact]
    public void Gravar_troca_o_arquivo_por_inteiro_e_nao_deixa_temporario()
    {
        using var temp = new TempFolder();
        var directory = temp.SubFolder("config");
        var file = new AppSettingsFile(directory);
        file.Save(AppSettings.Default with { Concurrency = 3 });

        file.Save(new AppSettings(4) { SpendLimit = 80m });

        Assert.Equal(80m, file.Load().SpendLimit);
        Assert.Equal([file.Path], Directory.GetFiles(directory));
    }

    [Fact]
    public void Gravar_por_cima_de_um_app_json_ilegivel_guarda_uma_copia()
    {
        using var temp = new TempFolder();
        var directory = temp.SubFolder("config");
        var file = new AppSettingsFile(directory);
        File.WriteAllText(file.Path, "{ spendLimit: 50, quebrado");

        Assert.NotNull(file.ReadProblem());
        file.Save(AppSettings.Default);

        Assert.Equal("{ spendLimit: 50, quebrado", File.ReadAllText(file.Path + ".bad"));
        Assert.Null(file.ReadProblem());
    }

    [Fact]
    public void Cotacao_limite_aviso_e_qualidade_vao_e_voltam_do_app_json()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = new AppSettings(3, ThemeChoice.Dark)
        {
            BrlPerUsd = 5.43m,
            RateInformedOn = new DateOnly(2026, 10, 25),
            SpendLimit = 200m,
            WarnAbove = 25.5m,
            Quality = TranslationQuality.Fast,
        };

        file.Save(settings);
        var loaded = file.Load();

        Assert.Equal(settings, loaded);
        Assert.Equal(new ExchangeRate(5.43m, new DateOnly(2026, 10, 25)), loaded.Rate);
        Assert.Equal(new BudgetLimits(200m, 25.5m), loaded.Budget);
    }

}

[Trait("Risco", "R5")]
public class MoneyFormattingTests
{
    [Theory]
    [InlineData("5.40", 5.40)]
    [InlineData("R$ 5,4", 5.4)]
    [InlineData("1.250,00", 1250.00)]
    [InlineData("1.250", 1250)]
    [InlineData("  0,5 ", 0.5)]
    public void Le_o_valor_em_reais_como_a_pessoa_digita(string text, double expected)
    {
        Assert.True(MoneyParser.TryParse(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("cinco")]
    [InlineData("-5")]
    [InlineData("5,4,3")]
    [InlineData("US$ 5")]
    [InlineData("1.2.3")]
    public void Recusa_o_que_nao_e_valor(string text)
    {
        Assert.False(MoneyParser.TryParse(text, out _));
    }

}

[Trait("Risco", "R5")]
public class BudgetSettingsViewModelTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(-3));

    private static SettingsViewModel Create(TempFolder temp, TimeProvider? clock = null) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null,
            _ => throw new InvalidOperationException("a cotação e o orçamento não chamam a API nem a rede"), clock ?? new FakeTimeProvider(Today));

    private static AppSettings Saved(TempFolder temp) => new AppSettingsFile(temp.SubFolder("config")).Load();

    [Fact]
    public void Limite_e_aviso_gravam_no_app_json_e_vazio_tira_o_limite()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.SpendLimitText = "200";
        vm.WarnAboveText = "25,50";

        Assert.Equal(new BudgetLimits(200m, 25.5m), vm.Budget);
        Assert.Equal(new BudgetLimits(200m, 25.5m), Saved(temp).Budget);

        vm.SpendLimitText = string.Empty;

        Assert.Null(Saved(temp).SpendLimit);
        Assert.Null(vm.LimitError);
        Assert.Equal(25.5m, Saved(temp).WarnAbove);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("muito")]
    public void Limite_invalido_mostra_o_erro_e_guarda_o_valor_anterior(string text)
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        vm.SpendLimitText = "200";

        vm.SpendLimitText = text;
        vm.WarnAboveText = text;

        Assert.NotNull(vm.LimitError);
        Assert.NotNull(vm.WarnError);
        Assert.Equal(200m, Saved(temp).SpendLimit);
    }

    private sealed class NoPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }
}
