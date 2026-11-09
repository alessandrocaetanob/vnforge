using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Eval;
using VNForge.AI.Translation;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Bible;
using VNForge.Core.Eval;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public sealed class EvalViewModelTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeJudgeProvider _judge = new(unit => unit.Translation!.StartsWith("tradução", StringComparison.Ordinal) ? 9 : 4);
    private readonly List<string> _models = [];

    public void Dispose() => _temp.Dispose();

    private ProjectFolder Project(int approved = 30, int untranslated = 20)
    {
        var statuses = Enumerable.Repeat(TranslationStatus.Approved, approved).Concat(Enumerable.Repeat(TranslationStatus.Untranslated, untranslated)).ToArray();
        return ProjectFolder.Open(SampleProjects.Create(_temp.SubFolder($"vn{Guid.NewGuid():N}"), "Minha VN", statuses));
    }

    private EvalViewModel Vm(Func<DashboardContext>? context = null) => new()
    {
        Context = context ?? (() => DashboardContext.Default),
        TranslationProviderFactory = (model, _) =>
        {
            _models.Add(model);
            return model == ClaudeModels.Opus ? new FakeTranslationProvider(u => $"tradução {u.Source[2..]}") : new FakeTranslationProvider(u => $"[PT] {u.Source}");
        },
        JudgeProviderFactory = _ => _judge,
    };

    private static async Task<EvalViewModel> RunRound(EvalViewModel vm, ProjectFolder project)
    {
        await vm.LoadAsync(project);
        await vm.RunOpenCommand.ExecuteAsync(null);
        await vm.StartRunCommand.ExecuteAsync(null);
        await vm.Running;
        return vm;
    }

    [Fact]
    public async Task Sem_projeto_a_aba_fica_vazia()
    {
        var vm = Vm();

        await vm.LoadAsync(null);

        Assert.False(vm.HasProject);
        Assert.False(vm.ShowStrip);
        Assert.False(vm.ShowNoGold);
        Assert.False(vm.RunOpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task Le_as_linhas_aprovadas_e_oferece_a_primeira_rodada()
    {
        var vm = Vm();

        await vm.LoadAsync(Project());

        Assert.Equal("30 linhas aprovadas", vm.GoldText);
        Assert.Equal("30 linhas", vm.SampleText);
        Assert.Equal(3, vm.ConfigCount);
        Assert.True(vm.ShowStrip);
        Assert.True(vm.ShowNoRound);
        Assert.False(vm.HasRound);
        Assert.True(vm.RunOpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sem_linhas_aprovadas_diz_o_que_fazer_e_nao_deixa_rodar()
    {
        var vm = Vm();

        await vm.LoadAsync(Project(approved: 0));

        Assert.True(vm.ShowNoGold);
        Assert.False(vm.ShowStrip);
        Assert.Contains("Aprove algumas no Editor", vm.NoGoldText);
        Assert.False(vm.RunOpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task O_dialogo_estima_o_custo_de_cada_configuracao_antes_de_rodar()
    {
        var vm = Vm();
        await vm.LoadAsync(Project());

        await vm.RunOpenCommand.ExecuteAsync(null);

        Assert.True(vm.IsRunOpen);
        Assert.Equal(3, vm.RunItems.Count);
        Assert.All(vm.RunItems, i => Assert.StartsWith("R$ ", i.CostText));
        Assert.True(vm.RunItems[0].Brl > vm.RunItems[2].Brl);
        Assert.Equal("Rodar 3 configurações", vm.RunLabel);
        Assert.StartsWith("Custo (aproximado): R$ ", vm.RunCostText);
        Assert.StartsWith("Já gasto no projeto: R$ 0,00", vm.RunSpentText);
        Assert.True(vm.CanRun);
        Assert.Empty(_models);
        Assert.Empty(_judge.ReceivedBatches);
    }

    [Fact]
    public async Task Tirar_configuracao_ou_juiz_baixa_o_custo_e_muda_o_botao()
    {
        var vm = Vm();
        await vm.LoadAsync(Project());
        await vm.RunOpenCommand.ExecuteAsync(null);
        var all = vm.AskBrl;

        vm.RunItems[0].IsSelected = false;
        var withoutFirst = vm.AskBrl;
        vm.UseJudge = false;

        Assert.True(withoutFirst < all);
        Assert.True(vm.AskBrl < withoutFirst);
        Assert.Equal("Rodar 2 configurações", vm.RunLabel);
        vm.RunItems[1].IsSelected = false;
        Assert.Equal("Rodar 1 configuração", vm.RunLabel);
        vm.RunItems[2].IsSelected = false;
        Assert.False(vm.CanRun);
        Assert.False(vm.StartRunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sem_chave_ou_acima_do_limite_o_botao_de_rodar_fica_desligado()
    {
        var noKey = Vm(() => new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false));
        await noKey.LoadAsync(Project());
        await noKey.RunOpenCommand.ExecuteAsync(null);
        Assert.True(noKey.KeyMissing);
        Assert.False(noKey.CanRun);

        var tight = Vm(() => new DashboardContext(ExchangeRate.Placeholder, new BudgetLimits(0.001m, null), HasApiKey: true));
        await tight.LoadAsync(Project());
        await tight.RunOpenCommand.ExecuteAsync(null);
        Assert.True(tight.IsOverLimit);
        Assert.False(tight.CanRun);
        Assert.Contains("limite de gasto", tight.OverLimitText);
    }

    [Fact]
    public async Task O_valor_de_aviso_so_avisa_e_ainda_deixa_rodar()
    {
        var vm = Vm(() => new DashboardContext(ExchangeRate.Placeholder, new BudgetLimits(null, 0.001m), HasApiKey: true));
        await vm.LoadAsync(Project());

        await vm.RunOpenCommand.ExecuteAsync(null);

        Assert.True(vm.AboveWarning);
        Assert.True(vm.CanRun);
    }

    [Fact]
    public async Task Cada_rodada_sorteia_com_semente_nova_e_grava_a_semente_para_reproduzir()
    {
        var project = Project(approved: 200);
        var vm = Vm();
        await vm.LoadAsync(project);

        var seeds = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            await vm.RunOpenCommand.ExecuteAsync(null);
            await vm.StartRunCommand.ExecuteAsync(null);
            await vm.Running;
            seeds.Add(EvalStore.LoadLatest(project)!.Seed);
        }

        Assert.NotEqual(seeds[0], seeds[1]);
        Assert.All(seeds, seed => Assert.True(seed > 0));
    }

    [Fact]
    public async Task Rodar_mostra_o_relatorio_sem_gravar_traducao_no_projeto()
    {
        var project = Project();
        var before = File.ReadAllText(project.TextPath("yst00001"));
        var vm = Vm();

        await RunRound(vm, project);

        Assert.True(vm.HasRound);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsRunOpen);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("A", vm.Rows[0].Letter);
        Assert.Equal("Opus 5.5", vm.Rows[0].ModelName);
        Assert.Equal("100,0", vm.Rows[0].ChrfText);
        Assert.Equal("9,0", vm.Rows[0].JudgeText);
        Assert.True(double.Parse(vm.Rows[1].ChrfText.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) < 100);
        Assert.StartsWith("Rodada de ", vm.RoundTitle);
        Assert.Equal("30 linhas sorteadas do conjunto ouro · mesma amostra em todas as configurações", vm.RoundSubtitle);
        Assert.Contains("20 linhas que faltam", vm.Footnote);
        Assert.Equal(3, vm.Points.Count);
        Assert.Contains("terminou", vm.Message);
        Assert.False(string.IsNullOrWhiteSpace(vm.Reading));
        Assert.Equal(before, File.ReadAllText(project.TextPath("yst00001")));
        Assert.NotNull(EvalStore.LoadLatest(project));
        Assert.Equal([ClaudeModels.Opus, ClaudeModels.Sonnet, ClaudeModels.Haiku], _models);
    }

    [Fact]
    public async Task Uma_falha_do_provedor_vira_mensagem_em_portugues_e_nao_derruba_a_aba()
    {
        var vm = Vm();
        vm.TranslationProviderFactory = (_, _) => throw new InvalidOperationException("sem chave");
        await vm.LoadAsync(Project());
        await vm.RunOpenCommand.ExecuteAsync(null);

        await vm.StartRunCommand.ExecuteAsync(null);
        await vm.Running;

        Assert.False(vm.IsRunning);
        Assert.False(vm.HasRound);
        Assert.StartsWith("A rodada parou: ", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_rodada_as_cegas_mostra_o_original_e_dois_textos_sem_dizer_a_configuracao()
    {
        var vm = await RunRound(Vm(), Project());

        Assert.True(vm.CanBlind);
        Assert.Equal("Começar às cegas", vm.BlindActionText);
        vm.BlindStartCommand.Execute(null);

        Assert.True(vm.IsBlind);
        Assert.False(vm.ShowReport);
        Assert.Equal("Par 1 de 12", vm.PairTitle);
        Assert.StartsWith("原文", vm.OriginalText);
        Assert.Matches(@"^ORIGINAL · CENA 1, LINHA \d{4}$", vm.OriginalMeta);
        Assert.Equal(1d / 12, vm.PairFraction, 6);
        Assert.NotEqual(vm.TextA, vm.TextB);
        Assert.Contains("de 3 linhas", vm.FitA);
        Assert.Equal("você escolhe sem saber qual configuração gerou cada texto", vm.BlindSubtitle);
    }

    [Fact]
    public async Task Cada_resposta_avanca_o_par_grava_na_rodada_e_o_fim_mostra_o_placar()
    {
        var project = Project();
        var vm = await RunRound(Vm(), project);
        vm.BlindStartCommand.Execute(null);

        vm.ChooseFirstCommand.Execute(null);
        Assert.Equal("Par 2 de 12", vm.PairTitle);
        vm.ChooseSecondCommand.Execute(null);
        vm.ChooseTieCommand.Execute(null);
        vm.ChooseNeitherCommand.Execute(null);
        vm.SkipPairCommand.Execute(null);
        Assert.Equal(4, EvalStore.LoadLatest(project)!.PairsDone);

        for (var i = 0; i < 7; i++)
        {
            vm.ChooseFirstCommand.Execute(null);
        }

        Assert.False(vm.IsBlind);
        Assert.True(vm.ShowReport);
        Assert.Equal("11 de 12 pares feitos", vm.BlindSummary);
        Assert.Contains("Sobrou 1 par que você pulou", vm.Message);
        Assert.NotEqual("sem rodada", vm.Rows[0].BlindText);
        Assert.All(vm.Rows.Where(r => r.BlindText != "não rodou"), r => Assert.Matches(@"^\d+ de \d+$", r.BlindText));
    }

    [Fact]
    public async Task As_respostas_voltam_quando_o_projeto_e_lido_de_novo()
    {
        var project = Project();
        var vm = await RunRound(Vm(), project);
        vm.BlindStartCommand.Execute(null);
        vm.ChooseFirstCommand.Execute(null);
        vm.ChooseFirstCommand.Execute(null);

        var again = Vm();
        await again.LoadAsync(project);

        Assert.Equal("2 de 12 pares feitos", again.BlindSummary);
        Assert.Equal("Continuar às cegas", again.BlindActionText);
        Assert.Equal(3, again.Rows.Count);
    }

    [Fact]
    public async Task Usar_grava_a_escolha_para_o_m4_e_a_linha_ganha_o_selo()
    {
        var project = Project();
        var vm = await RunRound(Vm(), project);
        var suggested = EvalReading.Recommended(EvalStore.LoadLatest(project)!)!.Config.Id;

        Assert.Equal($"Usar {suggested} no M4", vm.UseLabel);
        vm.SelectRowCommand.Execute(vm.Rows[2]);
        Assert.Equal("Usar C no M4", vm.UseLabel);
        vm.UseCommand.Execute(null);

        Assert.Equal("C", EvalStore.LoadLatest(project)!.ChosenConfigId);
        Assert.True(vm.Rows[2].IsChosen);
        Assert.False(vm.Rows[0].IsChosen);
        Assert.Contains("C ficou como a escolhida para o M4", vm.Message);
        var ai = project.LoadConfig().Ai;
        Assert.Equal((ClaudeModels.Haiku, "low", 15), (ai.Model, ai.Effort, ai.ContextBefore));
    }

    [Fact]
    public async Task Rodar_no_terminal_pede_para_copiar_o_comando()
    {
        var vm = Vm();
        var raised = 0;
        vm.TerminalCommandRequested += (_, _) => raised++;

        vm.CopyTerminalCommandCommand.Execute(null);

        Assert.Equal(1, raised);
        Assert.Equal("vnf eval", vm.TerminalCommand);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Sem_juiz_a_nota_sai_como_sem_juiz()
    {
        var vm = Vm();
        await vm.LoadAsync(Project());
        await vm.RunOpenCommand.ExecuteAsync(null);
        vm.UseJudge = false;

        await vm.StartRunCommand.ExecuteAsync(null);
        await vm.Running;

        Assert.All(vm.Rows, r => Assert.Equal("sem juiz", r.JudgeText));
        Assert.Empty(_judge.ReceivedBatches);
    }

    [Fact]
    public async Task A_barra_da_rodada_as_cegas_conta_o_par_que_esta_na_tela()
    {
        var vm = await RunRound(Vm(), Project());
        vm.BlindStartCommand.Execute(null);
        for (var i = 0; i < 6; i++)
        {
            vm.ChooseFirstCommand.Execute(null);
        }

        Assert.Equal("Par 7 de 12", vm.PairTitle);
        Assert.Equal(7d / 12, vm.PairFraction, 6);
    }

    [Fact]
    public async Task O_original_diz_para_quem_a_fala_vai_pelo_elenco_da_cena_na_biblia()
    {
        var project = Project();
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select(u => u with { Speaker = "美咲" })]);
        project.SaveCharacters([new CharacterEntry("美咲", "Misaki"), new CharacterEntry("相沢", "Aizawa"), new CharacterEntry("佐倉", "Sakura")]);
        project.SaveBible(GameBible.From([new BibleScene("yst00001") { Characters = ["Misaki", "Aizawa", "Sakura"] }]));
        var vm = await RunRound(Vm(), project);

        vm.BlindStartCommand.Execute(null);

        Assert.Equal("Misaki → Aizawa e Sakura", vm.OriginalSpeaker);
    }

    [Fact]
    public async Task Sem_elenco_na_biblia_o_original_mostra_so_quem_fala_com_o_nome_do_cadastro()
    {
        var project = Project();
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select(u => u with { Speaker = "美咲" })]);
        project.SaveCharacters([new CharacterEntry("美咲", "Misaki")]);
        var vm = await RunRound(Vm(), project);

        vm.BlindStartCommand.Execute(null);

        Assert.Equal("Misaki", vm.OriginalSpeaker);
    }

    [Fact]
    public async Task Falante_sem_cadastro_aparece_sozinho_mesmo_com_elenco_na_biblia()
    {
        var project = Project();
        project.SaveText("yst00001", [.. project.LoadText("yst00001").Select(u => u with { Speaker = "美咲" })]);
        project.SaveBible(GameBible.From([new BibleScene("yst00001") { Characters = ["Misaki", "Aizawa"] }]));
        var vm = await RunRound(Vm(), project);

        vm.BlindStartCommand.Execute(null);

        Assert.Equal("美咲", vm.OriginalSpeaker);
    }

    [Fact]
    public async Task A_barra_de_chrF_da_tabela_usa_a_janela_do_relatorio()
    {
        var vm = await RunRound(Vm(), Project());

        Assert.All(vm.Rows, r => Assert.InRange(r.ChrfShare, 0d, 1d));
        var best = vm.Rows.MaxBy(r => r.Chrf)!;
        Assert.Same(best, vm.Rows.MaxBy(r => r.ChrfShare));
    }

    [Fact]
    public void A_janela_do_chrF_vai_de_um_degrau_abaixo_do_menor_ao_maior_arredondado_para_cima()
    {
        var shares = EvalChrfBar.Shares([61.4, 60.9, 59.8, 54.3, 47]);

        Assert.Equal([0.856, 0.836, 0.792, 0.572, 0.28], shares.Select(s => Math.Round(s, 3)));
        Assert.Equal([0.6], EvalChrfBar.Shares([31]).Select(s => Math.Round(s, 3)));
        Assert.Empty(EvalChrfBar.Shares([]));
    }

    [Fact]
    public void O_eixo_de_custo_do_grafico_para_na_ultima_marca_que_cabe()
    {
        var points = new[] { new EvalChartPoint("A", 156.7, 61.4, false), new EvalChartPoint("E", 3.9, 47, false) };

        var (max, step) = EvalChart.XScale(points);

        Assert.Equal((170d, 50d), (max, step));
        Assert.Equal([0d, 50d, 100d, 150d], EvalChart.XTicks(max, step));
    }
}
