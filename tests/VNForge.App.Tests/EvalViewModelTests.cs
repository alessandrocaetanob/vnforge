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
    public async Task Voltar_da_rodada_as_cegas_mostra_o_relatorio_de_novo()
    {
        var vm = await RunRound(Vm(), Project());
        vm.BlindStartCommand.Execute(null);
        Assert.True(vm.IsBlind);

        vm.BlindBackCommand.Execute(null);

        Assert.False(vm.IsBlind);
        Assert.All(vm.Rows, r => Assert.Equal(!string.IsNullOrEmpty(r.Note), r.HasNote));
    }

    [Fact]
    public async Task A_rodada_as_cegas_mostra_o_original_e_dois_textos_sem_dizer_a_configuracao()
    {
        var vm = await RunRound(Vm(), Project());

        Assert.True(vm.CanBlind);
        Assert.Equal("Começar às cegas", vm.BlindActionText);
        vm.BlindStartCommand.Execute(null);

        Assert.True(vm.IsBlind);
        Assert.True(vm.HasPairs);
        Assert.False(vm.ShowReport);
        Assert.Equal("Par 1 de 12", vm.PairTitle);
        Assert.StartsWith("原文", vm.OriginalText);
        Assert.Matches(@"^Original · Cena 1, linha \d{4}$", vm.OriginalMeta);
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
    public void A_janela_do_chrF_vai_de_um_degrau_abaixo_do_menor_ao_maior_arredondado_para_cima()
    {
        var shares = EvalChrfBar.Shares([61.4, 60.9, 59.8, 54.3, 47]);

        Assert.Equal([0.856, 0.836, 0.792, 0.572, 0.28], shares.Select(s => Math.Round(s, 3)));
        Assert.Equal([0.6], EvalChrfBar.Shares([31]).Select(s => Math.Round(s, 3)));
        Assert.Empty(EvalChrfBar.Shares([]));
    }
}
