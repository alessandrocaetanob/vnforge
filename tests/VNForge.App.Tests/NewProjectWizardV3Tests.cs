using VNForge.AI.Cost;
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

    [Fact]
    public async Task O_primeiro_passo_diz_o_motor_em_linguagem_comum_e_deixa_o_tecnico_para_os_detalhes()
    {
        var vm = Create();

        await vm.SetGameFolderAsync(game);

        Assert.Equal("Feito com o motor YU-RIS", vm.EngineHeadline);
        Assert.Equal(
            [("Texto para traduzir", "2 arquivos de roteiro"), ("Cópia de segurança", "Feita antes de qualquer mudança no jogo")],
            vm.FoundFacts.Select(f => (f.Label, f.Value)));
        Assert.Equal(
            [("Arquivo de scripts", "ysbin.ypf · 2 scripts"), ("Chave XOR dos scripts", "0xD36FAC96"), ("Página de código", "932 (Shift-JIS)")],
            vm.DetectionFacts.Select(f => (f.Label, f.Value)));
        Assert.Equal("YU-RIS · YPF v500", vm.EngineTitle);

        var plain = vm.FoundFacts.SelectMany(f => new[] { f.Label, f.Value }).Append(vm.EngineHeadline).Append(vm.Steps[0].Subtitle);
        Assert.All(plain, text => Assert.DoesNotMatch("YPF|XOR|932|0x|ysbin|Shift", text));
    }

    [Fact]
    public async Task Um_unico_script_fica_no_singular()
    {
        var vm = Create(new SingleScriptEngine());

        await vm.SetGameFolderAsync(game);

        Assert.Equal("1 arquivo de roteiro", vm.FoundFacts[0].Value);
    }

    [Fact]
    public void Os_quatro_passos_usam_os_nomes_da_v3()
    {
        var vm = Create();

        Assert.Equal(["Pasta do jogo", "Idiomas e nome", "Acentos", "IA e gasto"], vm.Steps.Select(s => s.Title));
        Assert.Equal(
            ["Escolha a pasta instalada", "Nome, pasta e idiomas", "Como ficam os acentos", "Qualidade e limite de gasto"],
            vm.Steps.Select(s => s.Subtitle));
    }

    [Fact]
    public void Tres_cartoes_de_acento_mapeiam_para_as_estrategias_do_Core()
    {
        var vm = Create();
        var options = vm.EncodingStrategies.Options;

        Assert.Equal(["fold", "remap", "cp1252"], options.Select(o => o.Id));
        Assert.Equal(["Sem acentos", "Com acentos, fonte modificada", "Com acentos, ajuste no jogo"], options.Select(o => o.Title));
        Assert.Equal("fold", vm.EncodingStrategies.Selected.Id);
        Assert.Equal([true, false, true], options.Select(o => o.IsAvailable));
        Assert.Equal("Em breve", options[1].Badge);
    }

    [Fact]
    public async Task A_recomendacao_so_aparece_quando_a_engine_tem_patch_do_executavel()
    {
        var plain = Create();
        await plain.SetGameFolderAsync(game);
        var patching = Create(new PatchingEngine());
        await patching.SetGameFolderAsync(game);

        Assert.Equal(string.Empty, plain.EncodingStrategies.Options[2].Badge);
        Assert.Equal("Recomendada para este jogo", patching.EncodingStrategies.Options[2].Badge);
        Assert.Equal(string.Empty, patching.EncodingStrategies.Options[0].Badge);
    }

    [Fact]
    public void Cada_cartao_desenha_a_frase_de_teste_do_jeito_que_vai_sair()
    {
        var options = Create().EncodingStrategies.Options;

        string[] Lines(ChoiceOption o) => [.. o.Layout!.Boxes.Single().Lines.Select(l => l.Text)];

        Assert.Equal(["Voce nao vai acreditar no", "que aconteceu hoje a tarde!"], Lines(options[0]));
        Assert.Equal(["Você não vai acreditar no", "que aconteceu hoje à tarde!"], Lines(options[1]));
        Assert.Equal(Lines(options[1]), Lines(options[2]));
        Assert.All(options, o => Assert.Equal(3, o.Layout!.MaxLines));
    }

    [Fact]
    public void A_frase_de_teste_sem_acentos_vem_do_mesmo_algoritmo_do_build()
    {
        Assert.Equal(VNForge.Core.Text.CharacterFolder.Fold(AccentPreview.Sentence).Text, string.Join(' ', AccentPreview.Layout("fold").Boxes[0].Lines.Select(l => l.Text)));
        Assert.Equal(AccentPreview.Sentence, string.Join(' ', AccentPreview.Layout("cp1252").Boxes[0].Lines.Select(l => l.Text)));
    }

    [Fact]
    public async Task As_tres_qualidades_vem_da_Rapida_a_Caprichada_com_a_salva_marcada()
    {
        var vm = await AtAiStepAsync();

        Assert.Equal(["Rápida", "Equilibrada", "Caprichada"], vm.Qualities.Select(q => q.Title));
        Assert.Equal(["Caprichada"], vm.Qualities.Where(q => q.IsSelected).Select(q => q.Title));
        Assert.Equal(ProjectChoicesDefaultModel, vm.Models.Selected.Id);
    }

    private static string ProjectChoicesDefaultModel => VNForge.Core.Projects.ProjectChoices.DefaultAi.Model;

    [Fact]
    public async Task Escolher_a_qualidade_troca_o_modelo_e_o_modelo_exato_troca_a_qualidade()
    {
        var vm = await AtAiStepAsync();

        vm.Qualities.Single(q => q.Title == "Rápida").IsSelected = true;
        Assert.Equal("claude-haiku-5-5", vm.Models.Selected.Id);
        Assert.Equal("claude-haiku-5-5", vm.BuildOptions().Ai!.Model);

        vm.Models.Select("claude-sonnet-5-5");
        Assert.Equal(["Equilibrada"], vm.Qualities.Where(q => q.IsSelected).Select(q => q.Title));

        vm.Models.Select("claude-fable-5-1");
        Assert.DoesNotContain(vm.Qualities, q => q.IsSelected);
        Assert.Equal("claude-fable-5-1", vm.BuildOptions().Ai!.Model);
    }

    [Fact]
    public async Task O_gasto_do_jogo_inteiro_vem_em_reais_e_cresce_da_Rapida_para_a_Caprichada()
    {
        var vm = await AtAiStepAsync();

        Assert.All(vm.Qualities, q => Assert.True(q.HasEstimate));
        Assert.True(vm.Qualities[0].Price < vm.Qualities[1].Price);
        Assert.True(vm.Qualities[1].Price < vm.Qualities[2].Price);
        Assert.All(vm.Qualities, q => Assert.StartsWith("cerca de", q.Duration));
        Assert.Equal(2 * GameCostEstimator.LinesPerScript, vm.EstimatedLines);
        Assert.Contains("2 scripts", vm.EstimateNote);
    }

    [Fact]
    public async Task A_cotacao_digitada_refaz_as_contas_e_vale_hoje()
    {
        var vm = await AtAiStepAsync();
        vm.RateText = "5,00";
        var at5 = vm.Qualities.Select(q => q.Price).ToArray();

        vm.RateText = "10,00";

        Assert.Equal(at5.Select(p => p * 2m), vm.Qualities.Select(q => q.Price));
        Assert.Equal(new ExchangeRate(10m, new DateOnly(2026, 10, 25)), vm.Rate);
        Assert.Null(vm.RateError);
    }

    [Fact]
    public async Task Sem_cotacao_as_contas_usam_o_valor_de_exemplo_e_a_tela_avisa()
    {
        var vm = await AtAiStepAsync();

        Assert.False(vm.Rate.IsInformed);
        Assert.Equal(ExchangeRate.Placeholder.BrlPerUsd, vm.Rate.BrlPerUsd);
        Assert.Contains("só como exemplo", vm.RateNote);
    }

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
    public async Task Limite_invalido_mostra_o_erro_e_nao_deixa_criar()
    {
        var vm = await AtAiStepAsync();

        vm.LimitText = "0";

        Assert.Equal(BudgetInput.LimitInvalid, vm.LimitError);
        Assert.False(vm.NextCommand.CanExecute(null));
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
        Assert.Equal("Rápida", vm.Steps[3].Subtitle);
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
    public async Task Campos_vazios_nao_inventam_cotacao_nem_limite()
    {
        var file = SettingsFile();
        var vm = await AtAiStepAsync(file: file);

        await vm.NextCommand.ExecuteAsync(null);

        var saved = file.Load();
        Assert.Null(saved.BrlPerUsd);
        Assert.Null(saved.RateInformedOn);
        Assert.Null(saved.SpendLimit);
        Assert.Equal(TranslationQuality.Careful, saved.Quality);
    }

    [Fact]
    public async Task O_assistente_comeca_pelo_que_ja_esta_salvo_e_mantem_a_data_da_cotacao()
    {
        var file = SettingsFile();
        file.Save(new AppSettings(2) { BrlPerUsd = 5.43m, RateInformedOn = new DateOnly(2026, 1, 2), SpendLimit = 300m, Quality = TranslationQuality.Balanced });
        var vm = await AtAiStepAsync(file: file);

        Assert.Equal("5,43", vm.RateText);
        Assert.Equal("300,00", vm.LimitText);
        Assert.Equal(["Equilibrada"], vm.Qualities.Where(q => q.IsSelected).Select(q => q.Title));
        Assert.Equal("claude-sonnet-5-5", vm.BuildOptions().Ai!.Model);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 1, 2), file.Load().RateInformedOn);
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
        while (wizard.Step != WizardStep.Done)
        {
            wizard.RateText = "5,43";
            wizard.LimitText = "200";
            await wizard.NextCommand.ExecuteAsync(null);
        }

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

    [Fact]
    public async Task Sem_arquivo_de_ajustes_o_assistente_ainda_cria_o_projeto()
    {
        var vm = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()], clock: new FixedClock(Today));
        await vm.SetGameFolderAsync(game);
        vm.SetProjectDirectory(Path.Combine(temp.Path, "sem-ajustes"));
        vm.RateText = "5,43";
        while (vm.Step != WizardStep.Done)
        {
            await vm.NextCommand.ExecuteAsync(null);
        }

        Assert.NotNull(vm.Created);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Os_textos_do_passo_de_IA_nao_citam_modelo_nem_token()
    {
        var vm = await AtAiStepAsync();

        var texts = vm.Qualities.SelectMany(q => new[] { q.Title, q.Description, q.Duration })
            .Append(vm.KeyNote).Append(vm.RateNote).Append(vm.EstimateNote);

        Assert.All(texts, text => Assert.DoesNotMatch("(?i)claude-|opus|sonnet|haiku|token|DPAPI|ANTHROPIC", text));
        Assert.Contains("Ajustes", vm.KeyNote);
    }

    [Fact]
    public void Estimativa_sem_scripts_conhecidos_nao_inventa_numero()
    {
        Assert.Null(GameCostEstimator.LinesFor(null));
        Assert.Null(GameCostEstimator.LinesFor(0));
        Assert.Equal(GameCostEstimator.LinesPerScript * 312, GameCostEstimator.LinesFor(312));
        Assert.Empty(GameCostEstimator.DollarsByModel(0, ["claude-opus-5-5"]));
    }

    [Fact]
    public void O_custo_estimado_segue_o_do_Core_para_linhas_do_tamanho_do_exemplo()
    {
        var dollars = GameCostEstimator.DollarsByModel(1000, ["claude-haiku-5-5", "claude-opus-5-5"]);

        Assert.True(dollars["claude-haiku-5-5"] > 0m);
        Assert.Equal(dollars["claude-haiku-5-5"] * 40m, dollars["claude-opus-5-5"]);
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
