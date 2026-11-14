using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Engines;

namespace VNForge.App.Tests;

/// <summary>O assistente de novo projeto da interface v3 (#312): motor em linguagem comum, acentos com prévia, qualidade com custo, cotação e limite.</summary>
[Trait("Risco", "R5")]
[Trait("Risco", "R2")]
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
