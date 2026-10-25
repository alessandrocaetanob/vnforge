using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

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

    [Fact]
    public void Arquivo_antigo_sem_orcamento_vale_como_sem_limite_e_cotacao_nao_informada()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        File.WriteAllText(file.Path, """{ "concurrency": 3, "theme": "dark" }""");

        var loaded = file.Load();

        Assert.Null(loaded.BrlPerUsd);
        Assert.False(loaded.Rate.IsInformed);
        Assert.Equal(BudgetLimits.None, loaded.Budget);
        Assert.Equal(TranslationQuality.Careful, loaded.Quality);
    }

    [Theory]
    [InlineData("""{ "exchangeRate": 0, "spendLimit": -5, "warnAbove": "muito", "exchangeRateOn": "ontem", "quality": "ótima" }""")]
    [InlineData("""{ "exchangeRate": 5000, "spendLimit": null, "warnAbove": [1], "quality": 3 }""")]
    public void Valores_invalidos_do_orcamento_voltam_ao_padrao_sem_derrubar_o_app(string json)
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        File.WriteAllText(file.Path, json);

        var loaded = file.Load();

        Assert.Null(loaded.BrlPerUsd);
        Assert.Null(loaded.RateInformedOn);
        Assert.Null(loaded.SpendLimit);
        Assert.Null(loaded.WarnAbove);
        Assert.Equal(TranslationQuality.Careful, loaded.Quality);
    }

    [Fact]
    public void Data_da_cotacao_sem_a_cotacao_e_ignorada()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        File.WriteAllText(file.Path, """{ "exchangeRateOn": "2026-10-25" }""");

        Assert.Null(file.Load().RateInformedOn);
    }
}

public class MoneyFormattingTests
{
    [Theory]
    [InlineData("5,40", 5.40)]
    [InlineData("5.40", 5.40)]
    [InlineData("R$ 5,4", 5.4)]
    [InlineData("1.250,00", 1250.00)]
    [InlineData("1.250", 1250)]
    [InlineData("200", 200)]
    [InlineData("  0,5 ", 0.5)]
    public void Le_o_valor_em_reais_como_a_pessoa_digita(string text, double expected)
    {
        Assert.True(MoneyParser.TryParse(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cinco")]
    [InlineData("-5")]
    [InlineData("5,4,3")]
    [InlineData("US$ 5")]
    [InlineData("12.34,56")]
    [InlineData("1.2.3")]
    [InlineData("1234.567,00")]
    public void Recusa_o_que_nao_e_valor(string text)
    {
        Assert.False(MoneyParser.TryParse(text, out _));
    }

    [Fact]
    public void Formata_para_digitar_de_novo()
    {
        Assert.Equal("5,40", MoneyParser.Format(5.4m));
        Assert.Equal("1.250,00", MoneyParser.Format(1250m));
    }

    [Theory]
    [InlineData(0.0, "R$ 0,00")]
    [InlineData(0.004, "menos de R$ 0,01")]
    [InlineData(0.01, "R$ 0,01")]
    [InlineData(142.556, "R$ 142,56")]
    [InlineData(1254.0, "R$ 1.254,00")]
    public void Reais_de_estimativa_nao_viram_zero_quando_ha_gasto(double value, string expected)
    {
        Assert.Equal(expected, Money.BrlEstimate((decimal)value));
    }

    [Theory]
    [InlineData(0, "menos de 1 min")]
    [InlineData(20, "menos de 1 min")]
    [InlineData(6 * 60, "cerca de 6 min")]
    [InlineData(59 * 60, "cerca de 59 min")]
    [InlineData(2 * 3600, "cerca de 2 h")]
    [InlineData(3600 + (32 * 60), "cerca de 1 h 30 min")]
    [InlineData(84 * 3600, "cerca de 84 h")]
    public void Tempo_aparece_sem_falsa_precisao(int seconds, string expected)
    {
        Assert.Equal(expected, ProjectLabels.Duration(TimeSpan.FromSeconds(seconds)));
    }
}

public class BudgetSettingsViewModelTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(-3));

    private static SettingsViewModel Create(TempFolder temp, TimeProvider? clock = null) =>
        new(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("config")), _ => null,
            _ => throw new InvalidOperationException("a cotação e o orçamento não chamam a API nem a rede"), clock ?? new FakeTimeProvider(Today));

    private static AppSettings Saved(TempFolder temp) => new AppSettingsFile(temp.SubFolder("config")).Load();

    private static async Task<(SettingsViewModel Vm, ProjectFolder Folder)> OpenAsync(TempFolder temp, int pending)
    {
        var statuses = Enumerable.Repeat(TranslationStatus.Untranslated, pending).Concat([TranslationStatus.Approved]).ToArray();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        var vm = Create(temp);
        vm.Load(ProjectSummaryReader.Read(root));
        await vm.EstimateLoading;
        return (vm, ProjectFolder.Open(root));
    }

    [Fact]
    public void Sem_cotacao_informada_usa_o_exemplo_e_diz_que_e_exemplo()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Equal(string.Empty, vm.ExchangeRateText);
        Assert.False(vm.Rate.IsInformed);
        Assert.Contains("Ainda não informada", vm.RateNote);
        Assert.Contains("R$ 5,40", vm.RateNote);
    }

    [Fact]
    public void Informar_a_cotacao_grava_com_a_data_de_hoje_e_avisa_a_janela()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        ExchangeRate? raised = null;
        vm.RateChanged += (_, rate) => raised = rate;

        vm.ExchangeRateText = "5,43";

        var expected = new ExchangeRate(5.43m, new DateOnly(2026, 10, 25));
        Assert.Equal(expected, raised);
        Assert.Equal(expected, vm.Rate);
        Assert.Equal(expected, Saved(temp).Rate);
        Assert.Null(vm.RateError);
        Assert.Contains("25/10/2026", vm.RateNote);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("0")]
    [InlineData("101")]
    public void Cotacao_invalida_mostra_o_erro_e_nao_grava(string text)
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var raised = 0;
        vm.RateChanged += (_, _) => raised++;

        vm.ExchangeRateText = text;

        Assert.NotNull(vm.RateError);
        Assert.Equal(0, raised);
        Assert.Null(Saved(temp).BrlPerUsd);
    }

    [Fact]
    public void Apagar_a_cotacao_volta_ao_exemplo()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        vm.ExchangeRateText = "5,43";

        vm.ExchangeRateText = string.Empty;

        Assert.False(vm.Rate.IsInformed);
        Assert.Null(Saved(temp).BrlPerUsd);
        Assert.Null(vm.RateError);
    }

    [Fact]
    public async Task Os_cartoes_de_qualidade_mostram_o_gasto_em_reais_com_a_cotacao_informada()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp, 2000);
        vm.ExchangeRateText = "6,00";

        var opus = vm.Qualities.Single(q => q.Profile.Quality == TranslationQuality.Careful);
        var expected = vm.Models.Single(m => m.Id == ClaudeModels.Opus).Estimate;

        Assert.True(opus.HasEstimate);
        Assert.Equal(Money.BrlEstimate(opus.Price), expected);
        Assert.Equal(2000, vm.PendingLines);
        Assert.Equal(ProjectLabels.Duration(opus.Profile.TimeFor(2000)), opus.Duration);

        var before = opus.Price;
        vm.ExchangeRateText = "3,00";

        Assert.Equal(before / 2m, opus.Price);
    }

    [Fact]
    public void Sem_projeto_os_cartoes_de_qualidade_nao_mostram_preco()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.All(vm.Qualities, q =>
        {
            Assert.False(q.HasEstimate);
            Assert.Equal(string.Empty, q.Duration);
        });
    }

    [Fact]
    public void Escolher_a_qualidade_troca_o_modelo_e_vira_o_padrao_do_usuario()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        Assert.Equal(TranslationQuality.Careful, vm.SelectedQuality?.Profile.Quality);

        vm.SelectedQuality = vm.Qualities.Single(q => q.Profile.Quality == TranslationQuality.Balanced);

        Assert.Equal(ClaudeModels.Sonnet, vm.SelectedModel?.Id);
        Assert.Equal([false, true, false], vm.Qualities.Select(q => q.IsSelected));
        Assert.Equal(TranslationQuality.Balanced, Saved(temp).Quality);
        Assert.Equal(ClaudeModels.Sonnet, Create(temp).SelectedModel?.Id);
    }

    [Fact]
    public void O_modelo_exato_manda_e_o_fable_nao_marca_nenhuma_qualidade()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.SelectedModel = vm.Models.Single(m => m.Id == ClaudeModels.Fable);

        Assert.Null(vm.SelectedQuality);
        Assert.All(vm.Qualities, q => Assert.False(q.IsSelected));
        Assert.Equal(TranslationQuality.Careful, Saved(temp).Quality);
    }

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
    [InlineData("-10")]
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

    [Fact]
    public async Task O_gasto_do_projeto_sai_em_reais_contra_o_limite()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, 10);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(1000, 500, 0, 0, 26.40m, 1));
        vm.Load(ProjectSummaryReader.Read(folder.Root));
        vm.ExchangeRateText = "5,00";
        vm.SpendLimitText = "300";

        Assert.Equal(132.0, vm.SpentBrl);
        Assert.Equal(300.0, vm.LimitBrl);
        Assert.Equal("R$ 132,00 de R$ 300,00", vm.SpentSummary);

        vm.SpendLimitText = string.Empty;

        Assert.Equal("R$ 132,00 neste projeto", vm.SpentSummary);
    }

    [Fact]
    public void Mudar_o_tema_ou_a_concorrencia_nao_perde_o_orcamento()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        vm.ExchangeRateText = "5,43";
        vm.SpendLimitText = "200";
        vm.WarnAboveText = "20";

        vm.SelectedTheme = vm.Themes.Single(t => t.Value == ThemeChoice.Dark);
        vm.Concurrency = 5;

        var saved = Saved(temp);
        Assert.Equal(5.43m, saved.BrlPerUsd);
        Assert.Equal(200m, saved.SpendLimit);
        Assert.Equal(20m, saved.WarnAbove);
        Assert.Equal(ThemeChoice.Dark, saved.Theme);
        Assert.Equal(5, saved.Concurrency);
    }

    [Fact]
    public async Task A_janela_repassa_a_cotacao_as_tarefas_e_o_gasto_da_barra_de_status_sai_em_reais()
    {
        using var temp = new TempFolder();
        var settings = Create(temp);
        var tasks = new BackgroundTaskService();
        var window = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new NoPicker(), TimeProvider.System), taskService: tasks, settings: settings);
        Assert.NotNull(window);

        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var item = tasks.Start("Traduzir", async (reporter, _) =>
        {
            reporter.AddCost(2m);
            started.SetResult();
            await gate.Task;
        });
        await started.Task;

        settings.ExchangeRateText = "5,00";

        Assert.Equal(5.00m, tasks.Rate.BrlPerUsd);
        Assert.Equal("R$ 10,00", window.Tasks.Current!.CostText);
        Assert.Equal("R$ 10,00", item.CostText);
        gate.SetResult();
        await item.Completion;
    }

    [Fact]
    public void Confirmar_hoje_renova_a_data_com_o_mesmo_valor()
    {
        using var temp = new TempFolder();
        var clock = new FakeTimeProvider(Today);
        var vm = Create(temp, clock);
        Assert.False(vm.ConfirmRateTodayCommand.CanExecute(null));
        vm.ExchangeRateText = "5,43";
        Assert.True(vm.ConfirmRateTodayCommand.CanExecute(null));
        clock.Advance(TimeSpan.FromDays(3));
        ExchangeRate? raised = null;
        vm.RateChanged += (_, rate) => raised = rate;

        vm.ConfirmRateTodayCommand.Execute(null);

        var expected = new ExchangeRate(5.43m, new DateOnly(2026, 10, 28));
        Assert.Equal(expected, raised);
        Assert.Equal(expected, Saved(temp).Rate);
        Assert.Contains("28/10/2026", vm.RateNote);
    }

    [Fact]
    public async Task O_custo_da_tarefa_acompanha_a_cotacao_quando_ela_muda()
    {
        var tasks = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var item = tasks.Start("Traduzir", async (reporter, _) =>
        {
            reporter.AddCost(2m);
            started.SetResult();
            await gate.Task;
        });
        await started.Task;
        tasks.Rate = new ExchangeRate(5m, new DateOnly(2026, 10, 25));
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        tasks.Rate = new ExchangeRate(6m, new DateOnly(2026, 10, 25));

        Assert.Equal("R$ 12,00", item.CostText);
        Assert.Contains(nameof(BackgroundTaskItem.CostText), changed);
        gate.SetResult();
        await item.Completion;
    }

    [Fact]
    public async Task Com_a_cotacao_de_exemplo_o_custo_da_tarefa_e_o_gasto_saem_marcados()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp, 10);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(1000, 500, 0, 0, 10m, 1));
        vm.Load(ProjectSummaryReader.Read(folder.Root));
        var tasks = new BackgroundTaskService();
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var item = tasks.Start("Traduzir", async (reporter, _) =>
        {
            reporter.AddCost(2m);
            started.SetResult();
            await gate.Task;
        });
        await started.Task;

        Assert.Equal("≈ R$ 10,80 (cotação de exemplo)", item.CostText);
        Assert.Equal("≈ R$ 54,00 neste projeto (cotação de exemplo)", vm.SpentSummary);

        vm.ExchangeRateText = "5,00";
        tasks.Rate = vm.Rate;

        Assert.Equal("R$ 10,00", item.CostText);
        Assert.Equal("R$ 50,00 neste projeto", vm.SpentSummary);
        gate.SetResult();
        await item.Completion;
    }

    private sealed class NoPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }
}
