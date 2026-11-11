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
    public async Task Pedido_acima_do_limite_desliga_o_botao_e_oferece_a_qualidade_que_cabe()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(folder.LoadAllText(), ["yst00001"]), Rate);
        var limit = 10m + quotes.For(TranslationQuality.Balanced).Brl + 0.01m;

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(limit, null)));
        vm.EditorPass = false;

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
    public async Task A_passada_de_editor_marcada_entra_na_conta_do_limite_e_desmarcar_a_tira()
    {
        var folder = Project();
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(folder.LoadAllText(), ["yst00001"]), Rate);
        var limit = 10m + quotes.For(TranslationQuality.Careful).Brl + 0.01m;

        var vm = await OpenAsync(folder, Settings(new BudgetLimits(limit, null)));

        Assert.True(vm.EditorPass);
        Assert.True(vm.IsOverLimit);
        Assert.False(vm.CanStart);
        Assert.Contains("passa do limite", vm.OverLimitText);

        vm.EditorPass = false;

        Assert.False(vm.IsOverLimit);
        Assert.True(vm.CanStart);

        vm.EditorPass = true;

        Assert.True(vm.IsOverLimit);
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
    public async Task Sem_cotacao_informada_o_aviso_diz_que_o_valor_e_de_exemplo()
    {
        var vm = new TranslateDialogViewModel();
        await vm.OpenAsync(Project(), "yst00001", new TranslateDialogSettings(ExchangeRate.Placeholder, BudgetLimits.None, TranslationQuality.Careful, true));

        Assert.Contains("exemplo", vm.RateNote);
        Assert.Equal(string.Empty, (await OpenAsync(Project("outro"), Settings())).RateNote);
    }

    [Fact]
    public async Task Durante_a_noite_a_nota_cita_o_resumo_da_cena_na_biblia_quando_o_projeto_tem_um()
    {
        var folder = Project();
        folder.SaveBible(VNForge.Core.Bible.GameBible.From([new VNForge.Core.Bible.BibleScene("yst00001") { Name = "Manhã de chuva", Summary = "Kenta e Ren chegam ao portão." }]));
        var vm = await OpenAsync(folder, Settings());

        vm.SelectedWhen = vm.Whens[1];

        Assert.EndsWith(" e do resumo da cena na bíblia.", vm.ContextText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_limite_de_gasto_vale_sobre_a_metade_do_preco_no_lote()
    {
        var folder = Project();
        var normal = await OpenAsync(folder, Settings());
        var careful = normal.Qualities.Single(q => q.Quality == TranslationQuality.Careful).Price;

        // O limite cabe a metade do pedido caprichado, mas não o pedido inteiro.
        var vm = await OpenAsync(folder, Settings(new BudgetLimits(careful * 0.6m, null)));
        Assert.True(vm.IsOverLimit);

        vm.SelectedWhen = vm.Whens[1];

        Assert.False(vm.IsOverLimit);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task Iniciar_na_noite_pede_o_lote_e_a_cena_de_exemplo_nao_tem_a_escolha()
    {
        var vm = await OpenAsync(Project(), Settings());
        TranslationRequest? asked = null;
        vm.StartRequested += (_, request) => asked = request;
        vm.SelectedWhen = vm.Whens[1];

        vm.StartCommand.Execute(null);

        Assert.True(asked!.Overnight);
        Assert.False(vm.IsOpen);

        var sample = await OpenAsync(Project("exemplo"), Settings() with { IsSample = true });
        Assert.False(sample.ShowWhen);
        Assert.False(sample.IsOvernight);
    }

    [Fact]
    public async Task Na_noite_a_passada_de_editor_desmarca_trava_e_nao_entra_no_pedido_nem_na_conta()
    {
        var folder = Project();
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(folder.LoadAllText(), ["yst00001"]), Rate);
        Directory.CreateDirectory(folder.CacheDirectory);
        AiUsageLog.TryRecord(folder, new AiUsageTotals(0, 0, 0, 0, 2m, 1));
        var limit = 10m + quotes.For(TranslationQuality.Careful).Brl * 0.5m + 0.01m;
        var vm = await OpenAsync(folder, Settings(new BudgetLimits(limit, null)));
        TranslationRequest? asked = null;
        vm.StartRequested += (_, request) => asked = request;
        Assert.True(vm.EditorPass);
        Assert.True(vm.IsOverLimit);

        vm.SelectedWhen = vm.Whens[1];

        Assert.False(vm.EditorPass);
        Assert.False(vm.CanChooseEditorPass);
        Assert.False(vm.IsOverLimit);
        vm.StartCommand.Execute(null);
        Assert.True(asked!.Overnight);
        Assert.False(asked.EditorPass);

        vm.SelectedWhen = vm.Whens[0];

        Assert.True(vm.EditorPass);
        Assert.True(vm.CanChooseEditorPass);
    }
}
