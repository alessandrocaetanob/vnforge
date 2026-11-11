using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Engines;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>O assistente de novo projeto da interface v3 (#312): motor em linguagem comum, acentos com prévia, qualidade com custo, cotação e limite.</summary>
public sealed class NewProjectWizardV3Tests : IDisposable
{
    private static readonly DateTimeOffset Today = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly string config;

    public NewProjectWizardV3Tests()
    {
        game = temp.SubFolder("jogo");
        config = temp.SubFolder("config");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
    }

    public void Dispose() => temp.Dispose();

    private AppSettingsFile SettingsFile() => new(config);

    private NewProjectWizardViewModel Create(IEngine? engine = null, AppSettingsFile? file = null) =>
        new(new FakeFolderPicker(null), [engine ?? new FakeEngine()], appSettings: file ?? SettingsFile(), clock: new FixedClock(Today));

    private async Task<NewProjectWizardViewModel> AtAiStepAsync(IEngine? engine = null, AppSettingsFile? file = null)
    {
        var vm = Create(engine, file);
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        while (vm.Step < WizardStep.Ai)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }

        return vm;
    }

    private static string ProjectChoicesDefaultModel => VNForge.Core.Projects.ProjectChoices.DefaultAi.Model;

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("500")]
    public async Task Cotacao_invalida_mostra_o_erro_e_nao_deixa_criar(string text)
    {
        var vm = await AtAiStepAsync();

        vm.RateText = text;

        Assert.Equal(BudgetInput.RateInvalid, vm.RateError);
        Assert.False(vm.NextCommand.CanExecute(null));
        vm.RateText = string.Empty;
        Assert.Null(vm.RateError);
        Assert.True(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Criar_grava_cotacao_limite_e_qualidade_no_app_json_pelo_mesmo_servico_dos_Ajustes()
    {
        var file = SettingsFile();
        file.Save(new AppSettings(5, ThemeChoice.Dark) { WarnAbove = 25m });
        var vm = await AtAiStepAsync(file: file);
        vm.RateText = "5,43";
        vm.LimitText = "R$ 200";
        vm.Qualities.Single(q => q.Title == "Rápida").IsSelected = true;

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("Qualidade e limite de gasto", vm.Steps[4].Subtitle);
        var saved = file.Load();
        Assert.Equal(5.43m, saved.BrlPerUsd);
        Assert.Equal(new DateOnly(2026, 10, 25), saved.RateInformedOn);
        Assert.Equal(200m, saved.SpendLimit);
        Assert.Equal(TranslationQuality.Fast, saved.Quality);
        Assert.Equal(5, saved.Concurrency);
        Assert.Equal(ThemeChoice.Dark, saved.Theme);
        Assert.Equal(25m, saved.WarnAbove);
    }

    [Fact]
    public async Task Se_o_app_json_nao_grava_o_projeto_fica_criado_e_a_tela_avisa()
    {
        var blocker = Path.Combine(temp.Path, "arquivo-no-lugar-da-pasta");
        File.WriteAllText(blocker, "x");
        var vm = await AtAiStepAsync(file: new AppSettingsFile(Path.Combine(blocker, "config")));
        vm.RateText = "5,43";
        var reloaded = false;
        vm.BudgetSaved += (_, _) => reloaded = true;

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.NotNull(vm.Created);
        Assert.StartsWith("O projeto foi criado, mas não deu para salvar", vm.ErrorMessage);
        Assert.False(reloaded);
    }

    [Fact]
    public async Task Se_reler_os_Ajustes_falha_depois_de_gravar_a_pessoa_nao_volta_ao_passo_do_nome()
    {
        var file = SettingsFile();
        var vm = await AtAiStepAsync(file: file);
        vm.RateText = "5,43";
        vm.BudgetSaved += (_, _) => throw new IOException("sem acesso");

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(WizardStep.Done, vm.Step);
        Assert.NotNull(vm.Created);
        Assert.Equal(5.43m, file.Load().BrlPerUsd);
        Assert.Contains("recarregar os Ajustes", vm.ErrorMessage);
        Assert.DoesNotContain("Ajuste o nome", vm.ErrorMessage);
    }

    [Fact]
    public async Task Os_Ajustes_ja_abertos_leem_de_novo_o_que_o_assistente_gravou()
    {
        var file = SettingsFile();
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), file, clock: new FixedClock(Today));
        var wizard = Create(file: file);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(Today));
        var main = new MainWindowViewModel(start, settings: settings, newProjectWizard: () => wizard);
        start.NewProjectCommand.Execute(null);
        await wizard.SetGameFolderAsync(game);
        wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn"));
        wizard.RateText = "5,43";
        wizard.LimitText = "200";
        wizard.Qualities.Single(q => q.Title == "Rápida").IsSelected = true;
        while (wizard.Step != WizardStep.Done)
        {
            await wizard.NextCommand.ExecuteAsync(null);
        }

        Assert.Equal(["Rápida"], settings.Qualities.Where(q => q.IsSelected).Select(q => q.Title));
        Assert.Equal("5,43", settings.ExchangeRateText);
        Assert.Equal("200,00", settings.SpendLimitText);
        Assert.Equal(5.43m, settings.Rate.BrlPerUsd);
        Assert.Equal(5.43m, main.TaskService.Rate.BrlPerUsd);

        settings.SpendLimitText = "250";

        var saved = file.Load();
        Assert.Equal(250m, saved.SpendLimit);
        Assert.Equal(5.43m, saved.BrlPerUsd);
    }

    [Fact]
    public async Task Cancelar_nao_grava_nada()
    {
        var file = SettingsFile();
        var vm = await AtAiStepAsync(file: file);
        vm.RateText = "5,43";
        vm.CancelCommand.Execute(null);

        Assert.False(File.Exists(file.Path));
    }

    [Theory]
    [InlineData("v90001", "VNDB v90001")]
    [InlineData("90001", "VNDB v90001")]
    [InlineData(null, "")]
    public async Task O_jogo_da_biblioteca_mostra_o_codigo_do_vndb_e_escolher_outra_pasta_o_apaga(string? vndbId, string label)
    {
        var picker = new FakeFolderPicker(game);
        var vm = new NewProjectWizardViewModel(picker, [new FakeEngine()], clock: new FixedClock(Today));
        vm.SetLibraryGame(new LibraryGameContext("Amaoto no Hakoniwa", vndbId, null));

        Assert.True(vm.HasLibraryGame);
        Assert.Equal(label, vm.LibraryVndbLabel);
        Assert.Equal(vndbId is not null, vm.HasLibraryVndb);

        await vm.BrowseGameCommand.ExecuteAsync(null);

        Assert.False(vm.HasLibraryGame);
        Assert.Equal(string.Empty, vm.LibraryVndbLabel);
    }

    [Fact]
    public void Estimativa_sem_scripts_conhecidos_nao_inventa_numero()
    {
        Assert.Null(GameCostEstimator.LinesFor(null));
        Assert.Null(GameCostEstimator.LinesFor(0));
        Assert.Equal(GameCostEstimator.LinesPerScript * 312, GameCostEstimator.LinesFor(312));
        Assert.Empty(GameCostEstimator.DollarsByModel(0, ["claude-opus-5-5"]));
    }

    private sealed class SingleScriptEngine : IEngine
    {
        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.Matched("fake", new Dictionary<string, string>
        {
            ["ypfVersion"] = "500",
            ["scriptKey"] = "0xD36FAC96",
            ["scriptsArchive"] = "ysbin.ypf",
            ["scripts"] = "1",
        });

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    private sealed class PatchingEngine : IEngine, IGamePatchProvider
    {
        private readonly FakeEngine inner = new();

        public string Id => inner.Id;

        public EngineDetection Detect(string gameDir) => inner.Detect(gameDir);

        public IReadOnlyList<IArchiveFormat> Archives => inner.Archives;

        public IReadOnlyList<IScriptFormat> Scripts => inner.Scripts;

        public IReadOnlyList<IGamePatch> Patches => inner.Patches;

        public IReadOnlyList<IGamePatch> CreatePatches(string executable) => [];
    }
}
