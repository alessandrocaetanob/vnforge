using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class TranslateDialogViewModelTests : IDisposable
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private static TextUnit Line(string key, int i, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"{key}:{i:D6}", TextUnitKind.Dialogue, "ハル", "今日はとてもいい天気ですね、散歩に行きませんか？", status == TranslationStatus.Untranslated ? null : "ok", status);

    /// <summary>Quatro scripts: yst00001 com 100 pendentes, yst00002 com 50, yst00003 sem pendentes e yst00004 com 20; todos na mesma faixa de capítulo.</summary>
    private ProjectFolder Project(string sub = "projeto")
    {
        var root = SampleProjects.Create(temp.SubFolder(sub), "Teste");
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [.. Enumerable.Range(0, 100).Select(i => Line("yst00001", i))]);
        folder.SaveText("yst00002", [.. Enumerable.Range(0, 50).Select(i => Line("yst00002", i))]);
        folder.SaveText("yst00003", [.. Enumerable.Range(0, 5).Select(i => Line("yst00003", i, TranslationStatus.Approved))]);
        folder.SaveText("yst00004", [.. Enumerable.Range(0, 20).Select(i => Line("yst00004", i))]);
        return folder;
    }

    private static TranslateDialogSettings Settings(BudgetLimits? limits = null, TranslationQuality quality = TranslationQuality.Careful, bool key = true) =>
        new(Rate, limits ?? BudgetLimits.None, quality, key);

    private static async Task<TranslateDialogViewModel> OpenAsync(ProjectFolder folder, TranslateDialogSettings settings, string? scene = "yst00001", IReadOnlyList<string>? chosen = null)
    {
        var vm = new TranslateDialogViewModel();
        await vm.OpenAsync(folder, scene, settings, chosen);
        return vm;
    }

    [Fact]
    public async Task Abre_na_cena_aberta_com_a_qualidade_padrao_dos_ajustes()
    {
        var vm = await OpenAsync(Project(), Settings(quality: TranslationQuality.Balanced));

        Assert.True(vm.IsOpen);
        Assert.False(vm.IsLoading);
        Assert.Equal(TranslationScope.Scene, vm.SelectedScope.Scope);
        Assert.Equal(TranslationQuality.Balanced, vm.SelectedQuality.Quality);
        Assert.Equal(100, vm.Lines);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task Limite_desconhecido_porque_o_app_json_nao_leu_pede_confirmacao_no_botao()
    {
        var vm = await OpenAsync(Project(), Settings() with { BudgetProblem = "app.json ilegível" });

        Assert.True(vm.HasBudgetProblem);
        Assert.Contains("limite de gasto", vm.BudgetProblemText);
        Assert.Equal("Traduzir mesmo assim", vm.StartLabel);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task Cena_sem_linhas_pendentes_abre_no_capitulo()
    {
        var vm = await OpenAsync(Project(), Settings(), scene: "yst00003");

        Assert.Equal(TranslationScope.Chapter, vm.SelectedScope.Scope);
        Assert.Equal(170, vm.Lines);
    }

    [Fact]
    public async Task Escopo_sem_linhas_pendentes_fica_desligado_e_diz_que_nao_ha_nada_para_traduzir()
    {
        var vm = await OpenAsync(Project(), Settings(), scene: "yst00003");

        var scene = vm.Scopes.Single(s => s.Scope == TranslationScope.Scene);
        Assert.False(scene.IsAvailable);
        Assert.Equal("Nada para traduzir aqui", scene.Summary);
        Assert.All(vm.Scopes.Where(s => s.Scope != TranslationScope.Scene), s => Assert.True(s.IsAvailable));
        Assert.False(scene.IsSelected);
        Assert.True(vm.Scopes.Single(s => s.IsSelected).IsAvailable);
    }

    [Fact]
    public async Task Os_escopos_ficam_ligados_enquanto_o_projeto_e_lido()
    {
        var vm = new TranslateDialogViewModel();

        Assert.All(vm.Scopes, s => Assert.True(s.IsAvailable));
        await vm.OpenAsync(Project(), "yst00001", Settings());
        Assert.All(vm.Scopes, s => Assert.True(s.IsAvailable));
    }

    [Fact]
    public async Task Cada_escopo_conta_as_linhas_pendentes_e_o_preco_acompanha()
    {
        var vm = await OpenAsync(Project(), Settings());

        Assert.Equal([100, 170, 170], vm.Scopes.Select(s => s.Lines));
        var scene = vm.SelectedQuality.Price;

        vm.SelectedScope = vm.Scopes.Single(s => s.Scope == TranslationScope.Game);

        Assert.Equal(170, vm.Lines);
        Assert.True(vm.SelectedQuality.Price > scene);
        Assert.Equal(vm.SelectedQuality.Price, vm.RequestBrl);
    }

    [Fact]
    public async Task Os_tres_cartoes_trazem_preco_e_tempo_sem_nome_de_modelo()
    {
        var vm = await OpenAsync(Project(), Settings());

        Assert.Equal(["Rápida", "Equilibrada", "Caprichada"], vm.Qualities.Select(q => q.Title));
        Assert.DoesNotContain(typeof(QualityChoice).GetProperties(), p => p.Name == "Model");
        Assert.True(vm.Qualities[0].Price < vm.Qualities[1].Price && vm.Qualities[1].Price < vm.Qualities[2].Price);
        Assert.All(vm.Qualities, q => Assert.False(string.IsNullOrEmpty(q.Duration)));
    }

    [Fact]
    public async Task Pedido_acima_do_limite_desliga_o_botao_e_oferece_a_qualidade_que_cabe()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(folder.LoadAllText(), ["yst00001"]), Rate);
        var limit = 10m + quotes.For(TranslationQuality.Balanced).Brl + 0.01m;

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(limit, null)));

        Assert.Equal(TranslationQuality.Careful, vm.SelectedQuality.Quality);
        Assert.True(vm.IsOverLimit);
        Assert.False(vm.CanStart);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.CanSwitchToFit);
        Assert.Contains("Equilibrada", vm.SwitchToFitText);
        Assert.Contains("passa do limite", vm.OverLimitText);

        vm.SwitchToFitCommand.Execute(null);

        Assert.Equal(TranslationQuality.Balanced, vm.SelectedQuality.Quality);
        Assert.False(vm.IsOverLimit);
        Assert.True(vm.CanStart);
        Assert.False(vm.CanSwitchToFit);
    }

    [Fact]
    public async Task Quando_nenhuma_qualidade_cabe_so_resta_aumentar_o_limite_ou_reduzir_o_escopo()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(10m, null)));

        Assert.True(vm.IsOverLimit);
        Assert.False(vm.CanSwitchToFit);
        Assert.False(vm.CanStart);
        Assert.Contains("Ajustes", vm.OverLimitText);
    }

    [Fact]
    public async Task Sem_limite_nada_passa_do_limite()
    {
        var vm = await OpenAsync(Project(), Settings());

        Assert.False(vm.IsOverLimit);
        Assert.Equal(0d, vm.LimitBrl);
    }

    [Fact]
    public async Task A_barra_mostra_o_gasto_o_pedido_e_o_limite()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(300m, null)));

        Assert.Equal(10d, vm.SpentBrl);
        Assert.Equal(300d, vm.LimitBrl);
        Assert.Equal((double)vm.RequestBrl, vm.AskBrl);
    }

    [Fact]
    public async Task Pedido_acima_do_valor_de_aviso_muda_o_botao_para_traduzir_mesmo_assim()
    {
        var vm = await OpenAsync(Project(), Settings(new BudgetLimits(null, 0.01m)));

        Assert.True(vm.AboveWarning);
        Assert.Equal("Traduzir mesmo assim", vm.StartLabel);
        Assert.True(vm.CanStart);

        var calm = await OpenAsync(Project("outro"), Settings());
        Assert.False(calm.AboveWarning);
        Assert.Equal("Traduzir", calm.StartLabel);
    }

    [Fact]
    public async Task Traduzir_entrega_o_pedido_com_os_scripts_do_escopo_e_fecha()
    {
        var vm = await OpenAsync(Project(), Settings(quality: TranslationQuality.Fast));
        vm.SelectedScope = vm.Scopes.Single(s => s.Scope == TranslationScope.Chapter);
        TranslationRequest? request = null;
        vm.StartRequested += (_, r) => request = r;

        vm.StartCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.NotNull(request);
        Assert.Equal(TranslationScope.Chapter, request.Scope);
        Assert.Equal(TranslationQuality.Fast, request.Quality);
        Assert.Equal(["yst00001", "yst00002", "yst00003", "yst00004"], request.ScriptKeys);
    }

    [Fact]
    public async Task Com_varias_cenas_escolhidas_a_primeira_resposta_e_as_cenas_escolhidas()
    {
        var vm = await OpenAsync(Project(), Settings(quality: TranslationQuality.Fast), chosen: ["yst00004", "yst00001", "yst00003"]);

        Assert.Equal([TranslationScope.Selection, TranslationScope.Chapter, TranslationScope.Game], vm.Scopes.Select(s => s.Scope));
        Assert.Equal("As cenas escolhidas (3)", vm.Scopes[0].Title);
        Assert.Equal(TranslationScope.Selection, vm.SelectedScope.Scope);
        Assert.Equal(120, vm.Lines);
        Assert.Equal("120 linhas para traduzir em 2 cenas", vm.Scopes[0].Summary);

        TranslationRequest? request = null;
        vm.StartRequested += (_, r) => request = r;
        vm.StartCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(TranslationScope.Selection, request.Scope);
        Assert.Equal(["yst00001", "yst00003", "yst00004"], request.ScriptKeys);
    }

    [Fact]
    public async Task Com_uma_cena_so_a_pergunta_continua_sendo_esta_cena()
    {
        var vm = await OpenAsync(Project(), Settings(), chosen: ["yst00001"]);

        Assert.Equal([TranslationScope.Scene, TranslationScope.Chapter, TranslationScope.Game], vm.Scopes.Select(s => s.Scope));
        Assert.Equal("Esta cena", vm.Scopes[0].Title);
    }

    [Fact]
    public async Task Reabrir_sem_cenas_escolhidas_volta_a_esta_cena()
    {
        var folder = Project();
        var vm = await OpenAsync(folder, Settings(), chosen: ["yst00001", "yst00002"]);

        await vm.OpenAsync(folder, "yst00001", Settings());

        Assert.Equal(TranslationScope.Scene, vm.Scopes[0].Scope);
    }

    [Fact]
    public async Task Cancelar_fecha_sem_pedir_nada()
    {
        var vm = await OpenAsync(Project(), Settings());
        var started = false;
        vm.StartRequested += (_, _) => started = true;

        vm.CancelCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.False(started);
    }

    [Fact]
    public async Task A_frase_diz_o_que_vai_junto_no_pedido()
    {
        var folder = Project();
        folder.SaveGlossary([new GlossaryTerm("魔法", "magia")]);
        folder.SaveCharacters([new CharacterEntry("ハル", "Haru", CharacterGender.Female, "calma")]);
        File.WriteAllText(Path.Combine(folder.Root, "style.md"), "Use você.");

        var vm = await OpenAsync(folder, Settings());

        Assert.Contains("glossário", vm.ContextText);
        Assert.Contains("voz dos personagens", vm.ContextText);
        Assert.Contains("guia de estilo", vm.ContextText);
        Assert.Contains("falas em volta", vm.ContextText);
        Assert.False(vm.HasMissingGender);
    }

    [Fact]
    public async Task Projeto_sem_glossario_nem_guia_diz_so_o_que_existe()
    {
        var vm = await OpenAsync(Project(), Settings());

        Assert.DoesNotContain("glossário", vm.ContextText);
        Assert.DoesNotContain("guia de estilo", vm.ContextText);
        Assert.Contains("falas em volta", vm.ContextText);
    }

    [Fact]
    public async Task Personagens_sem_genero_avisam_e_definir_agora_leva_ao_glossario()
    {
        var folder = Project();
        folder.SaveCharacters([new CharacterEntry("ハル", "Haru"), new CharacterEntry("ケン", "Ken", CharacterGender.Male), new CharacterEntry("ミキ", "Miki")]);
        var vm = await OpenAsync(folder, Settings());
        var asked = false;
        vm.GlossaryRequested += (_, _) => asked = true;

        Assert.True(vm.HasMissingGender);
        Assert.Contains("2 personagens sem gênero", vm.MissingGenderText);

        vm.DefineGendersCommand.Execute(null);

        Assert.True(asked);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Sem_chave_o_botao_fica_desligado_e_o_aviso_leva_aos_ajustes()
    {
        var vm = await OpenAsync(Project(), Settings(key: false));

        Assert.False(vm.CanStart);
        Assert.True(vm.KeyMissing);
        Assert.Contains("Ajustes", vm.KeyMissingText);
    }

    [Fact]
    public async Task Os_textos_da_janela_nao_falam_de_token_ypf_nem_nome_de_modelo()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var vm = await OpenAsync(folder, Settings(new BudgetLimits(10m, 0.01m)));

        var texts = new List<string> { vm.ContextText, vm.OverLimitText, vm.MissingGenderText, vm.KeyMissingText, vm.StartLabel, vm.RequestText, vm.SpentText, vm.RateNote };
        texts.AddRange(vm.Scopes.SelectMany(s => new[] { s.Title, s.Summary }));
        texts.AddRange(vm.Qualities.SelectMany(q => new[] { q.Title, q.Description, q.Duration }));

        foreach (var text in texts)
        {
            Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ypf", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("claude", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("opus", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Projeto_ilegivel_mostra_o_erro_em_vez_de_ficar_carregando()
    {
        var folder = Project();
        File.WriteAllText(folder.TextPath("yst00001"), "{ isto não é JSON");

        var vm = await OpenAsync(folder, Settings());

        Assert.False(vm.IsLoading);
        Assert.False(vm.CanStart);
        Assert.StartsWith("Não foi possível ler o projeto", vm.LoadError);
    }

    [Fact]
    public async Task Reabrir_numa_cena_com_linhas_nao_deixa_o_cartao_apagado_da_abertura_anterior()
    {
        var folder = Project();
        var vm = await OpenAsync(folder, Settings(), scene: "yst00003");
        Assert.False(vm.Scopes[0].IsAvailable);

        var reopening = vm.OpenAsync(folder, "yst00001", Settings());

        Assert.True(vm.Scopes[0].IsAvailable);
        Assert.Equal(0, vm.Scopes[0].Lines);
        Assert.Equal(string.Empty, vm.Scopes[0].Summary);
        await reopening;
        Assert.True(vm.Scopes[0].IsAvailable);
        Assert.Equal(100, vm.Scopes[0].Lines);
    }

    [Fact]
    public async Task Reabrir_com_a_leitura_falhando_nao_deixa_cartao_apagado_nem_resumo_velho()
    {
        var folder = Project();
        var vm = await OpenAsync(folder, Settings(), scene: "yst00003");
        Assert.False(vm.Scopes[0].IsAvailable);
        File.WriteAllText(folder.TextPath("yst00001"), "{ isto não é JSON");

        await vm.OpenAsync(folder, "yst00001", Settings());

        Assert.StartsWith("Não foi possível ler o projeto", vm.LoadError);
        Assert.All(vm.Scopes, scope => Assert.True(scope.IsAvailable));
        Assert.All(vm.Scopes, scope => Assert.Equal(string.Empty, scope.Summary));
    }

    [Fact]
    public async Task Reabrir_desliga_o_botao_ate_a_nova_leitura_terminar()
    {
        var folder = Project();
        var vm = await OpenAsync(folder, Settings());
        Assert.True(vm.CanStart);

        var reopening = vm.OpenAsync(folder, "yst00002", Settings());
        var duringLoad = vm.CanStart;
        await reopening;

        Assert.False(duringLoad);
        Assert.True(vm.CanStart);
        Assert.Equal(50, vm.Lines);
    }

    [Fact]
    public async Task Sem_cotacao_informada_o_aviso_diz_que_o_valor_e_de_exemplo()
    {
        var vm = new TranslateDialogViewModel();
        await vm.OpenAsync(Project(), "yst00001", new TranslateDialogSettings(ExchangeRate.Placeholder, BudgetLimits.None, TranslationQuality.Careful, true));

        Assert.Contains("exemplo", vm.RateNote);
        Assert.Equal(string.Empty, (await OpenAsync(Project("outro"), Settings())).RateNote);
    }
}
