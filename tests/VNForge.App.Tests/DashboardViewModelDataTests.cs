using VNForge.AI.Cost;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O que o Painel da v3 mostra: o próximo passo, a jornada, o sumário de capítulos, o ritmo, "Precisa de você", o gasto e o estado do jogo.</summary>
public class DashboardViewModelDataTests
{
    private const string Root = @"D:\Traducoes\minha-vn-ptbr";

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private static readonly ProjectSummary Summary = new("Minha VN — PT-BR", Root, "yuris", 500, "cp1252", "claude-opus-5-5", 3, new StatusCounts(0, 0, 0, 0, 0));

    private static List<TextUnit> Lines(string key, params (TranslationStatus Status, int Count)[] groups) =>
    [
        .. groups.SelectMany(g => Enumerable.Repeat(g.Status, g.Count))
            .Select((status, i) => new TextUnit($"{key}:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", status == TranslationStatus.Untranslated ? null : "tradução", status)),
    ];

    private static ProjectStatus StatusOf(params (string Key, List<TextUnit> Units)[] scripts) =>
        ProjectStatusReporter.Create(scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal));

    private static IReadOnlyList<PipelineStage> Stages(bool extract = true, int untranslated = 0, int awaiting = 0, bool build = false, bool inject = false) =>
    [
        new(PipelineStageKey.Extract, extract, extract ? 0 : 1),
        new(PipelineStageKey.Translate, untranslated == 0, untranslated),
        new(PipelineStageKey.Review, awaiting == 0 && untranslated == 0, awaiting),
        new(PipelineStageKey.Build, build, build ? 0 : 1),
        new(PipelineStageKey.Inject, inject, inject ? 0 : 1),
    ];

    private static ProjectDashboard Dashboard(ProjectStatus status, ScriptNames? names = null, AiUsageTotals? usage = null, bool build = false, bool inject = false) =>
        new(status, Stages(untranslated: status.Total.Untranslated, awaiting: ProjectDashboard.AwaitingReview(status.Total), build: build, inject: inject), usage ?? AiUsageTotals.Empty)
        {
            ScriptNames = names ?? ScriptNames.Empty,
            Qa = new QaTotals(0, 0),
        };

    private static async Task<DashboardViewModel> Load(ProjectDashboard dashboard, DashboardContext? context = null, ProjectSummary? summary = null)
    {
        var vm = new DashboardViewModel(_ => dashboard, new FixedClock(Now)) { Project = summary ?? Summary };
        if (context is not null)
        {
            vm.Context = () => context;
        }

        await vm.ReloadAsync();
        return vm;
    }

    private static ScriptNames TwoChapters() => ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
    {
        ["yst00001"] = new("manha", "userscript/01_cap1"),
        ["yst00002"] = new("tarde", "userscript/01_cap1"),
        ["yst00003"] = new("noite", "userscript/02_cap2"),
        ["yst00004"] = new("menu", "userscript/system"),
    });

    private static ProjectStatus Mixed() => StatusOf(
        ("yst00001", Lines("yst00001", (TranslationStatus.Approved, 21), (TranslationStatus.Reviewed, 18))),
        ("yst00002", Lines("yst00002", (TranslationStatus.Machine, 6))),
        ("yst00003", Lines("yst00003", (TranslationStatus.Machine, 17), (TranslationStatus.Untranslated, 38))),
        ("yst00004", Lines("yst00004", (TranslationStatus.Approved, 5))));

    [Fact]
    public async Task O_titulo_e_o_nome_do_projeto_sem_pasta_nem_engine()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal("Minha VN — PT-BR", vm.Title);
        Assert.DoesNotContain(Root, vm.MapSummary + vm.PaceText + string.Join(' ', vm.Steps.Select(s => s.Subtitle)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_sumario_traz_um_capitulo_por_linha_com_os_tres_totais_e_menus_e_nomes_no_fim()
    {
        var vm = await Load(Dashboard(Mixed(), TwoChapters()));

        Assert.Equal(["Capítulo 1", "Capítulo 2", "Menus e nomes"], vm.Chapters.Select(c => c.Name));
        Assert.Equal([1, 2, null], vm.Chapters.Select(c => c.Number));
        var first = vm.Chapters[0];
        Assert.Equal(39, first.Ready);
        Assert.Equal(6, first.ToReview);
        Assert.Equal(45, first.Total);
        Assert.True(vm.Chapters[2].IsExtra);
        Assert.Equal(55, vm.Chapters[1].Total);
        Assert.Equal(17, vm.Chapters[1].ToReview);
    }

    [Fact]
    public async Task O_capitulo_aberto_ganha_a_nota_Voce_esta_aqui_e_o_erro_com_o_nome_da_cena()
    {
        var names = TwoChapters().Rename("yst00002", "Manhã de chuva");
        var dashboard = new ProjectDashboard(Mixed(), Stages(), AiUsageTotals.Empty)
        {
            ScriptNames = names,
            Qa = new QaTotals(2, 0),
            QaByScript = new Dictionary<string, QaTotals> { ["yst00002"] = new(1, 0), ["yst00003"] = new(1, 0) },
        };

        var vm = await Load(dashboard, summary: Summary with { LastEditedScript = "yst00002" });

        var here = vm.Chapters[0];
        Assert.True(here.IsHere);
        Assert.Equal("Você está aqui · 1 erro em Manhã de chuva", here.Note);
        Assert.False(vm.Chapters[1].IsHere);
        Assert.Equal("1 erro trava o teste", vm.Chapters[1].Note);
    }

    [Fact]
    public async Task Varios_erros_no_capitulo_e_avisos_sem_erro_viram_notas_curtas()
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(), AiUsageTotals.Empty)
        {
            ScriptNames = TwoChapters(),
            QaByScript = new Dictionary<string, QaTotals> { ["yst00001"] = new(2, 0), ["yst00002"] = new(1, 0), ["yst00003"] = new(0, 3) },
        };

        var vm = await Load(dashboard);

        Assert.Equal("3 erros travam o teste", vm.Chapters[0].Note);
        Assert.Equal("3 avisos", vm.Chapters[1].Note);
        Assert.Equal(string.Empty, vm.Chapters[2].Note);
    }

    [Fact]
    public async Task No_limite_de_gasto_a_nota_diz_que_a_IA_parou_no_primeiro_capitulo_com_japones()
    {
        var usage = new AiUsageTotals(1, 1, 0, 0, 60m, 1);
        var context = new DashboardContext(new ExchangeRate(5m, new DateOnly(2026, 10, 22)), new BudgetLimits(300m, null), true);

        var vm = await Load(Dashboard(Mixed(), TwoChapters(), usage), context);

        Assert.Equal(string.Empty, vm.Chapters[0].Note);
        Assert.Equal("A IA parou no limite de gasto", vm.Chapters[1].Note);
        Assert.Contains(vm.Todos, t => t.Title == "A IA parou no limite de gasto");
    }

    [Fact]
    public async Task O_proximo_passo_revisa_o_primeiro_capitulo_com_linhas_da_IA()
    {
        var vm = await Load(Dashboard(Mixed(), TwoChapters()));
        var requested = new List<DashboardNavigation>();
        vm.NavigationRequested += (_, request) => requested.Add(request);

        Assert.True(vm.HasNextStep);
        Assert.Equal("Revisar o capítulo 1", vm.NextStepLabel);
        vm.OpenNextStepCommand.Execute(null);

        Assert.Equal([new DashboardNavigation(NavKey.Editor, "yst00002")], requested);
    }

    [Fact]
    public async Task O_proximo_passo_aponta_o_capitulo_onde_a_pessoa_esta_quando_ele_tem_o_que_fazer()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", (TranslationStatus.Machine, 3))),
            ("yst00002", Lines("yst00002", (TranslationStatus.Machine, 3))),
            ("yst00003", Lines("yst00003", (TranslationStatus.Approved, 3))));
        var names = ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
        {
            ["yst00001"] = new("a", "userscript/01_cap1"),
            ["yst00002"] = new("b", "userscript/02_cap2"),
            ["yst00003"] = new("c", "userscript/03_cap3"),
        });

        var here = await Load(Dashboard(status, names), summary: Summary with { LastEditedScript = "yst00002" });
        var done = await Load(Dashboard(status, names), summary: Summary with { LastEditedScript = "yst00003" });

        Assert.Equal("Revisar o capítulo 2", here.NextStepLabel);
        Assert.True(here.Chapters[1].IsHere);
        Assert.Equal("Revisar o capítulo 1", done.NextStepLabel);
    }

    [Fact]
    public async Task O_passo_de_teste_e_o_cartao_do_jogo_leem_o_mesmo_game_state()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 2))));
        var inGame = new GameInGame(GameTestKind.Translation, new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero), null, null, null);

        var vm = await Load(new ProjectDashboard(status, Stages(), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(0, 0),
            GameState = new GameStateFile(GameStateFile.CurrentVersion, inGame, null),
        });

        Assert.Equal("Testado em 22/10", vm.Steps[4].Subtitle);
        Assert.Equal("O jogo está com a tradução de 22/10", vm.GameTitle);
        Assert.Equal(vm.Steps[4].Subtitle, vm.LastTestText);
        Assert.Equal(PipelineStepState.Done, vm.Steps[4].State);
        Assert.Equal("Testar de novo no jogo", vm.NextStepLabel);
    }

    [Fact]
    public async Task Erro_sem_nome_de_cena_no_capitulo_aberto_diz_numa_cena_e_nao_a_chave()
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(), AiUsageTotals.Empty)
        {
            QaByScript = new Dictionary<string, QaTotals> { ["yst00001"] = new(1, 0) },
        };

        var vm = await Load(dashboard, summary: Summary with { LastEditedScript = "yst00001" });

        Assert.Equal("Você está aqui · 1 erro numa cena", vm.Chapters[0].Note);
    }

    [Fact]
    public async Task Falha_ao_montar_o_Painel_mostra_o_erro_e_nao_deixa_a_tela_lendo_para_sempre()
    {
        var vm = new DashboardViewModel(_ => Dashboard(Mixed())) { Project = Summary };
        vm.Context = () => throw new InvalidOperationException("ajustes ilegíveis");

        await vm.ReloadAsync();

        Assert.False(vm.IsLoading);
        Assert.Contains("ajustes ilegíveis", vm.ErrorMessage);
        Assert.Empty(vm.Chapters);
    }

    [Fact]
    public async Task Capitulo_com_nome_da_pessoa_aparece_entre_aspas_no_botao()
    {
        var names = TwoChapters().WithGroup("yst00001", "Manhã de chuva").WithGroup("yst00002", "Manhã de chuva");

        var vm = await Load(Dashboard(Mixed(), names));

        Assert.Equal("Revisar “Manhã de chuva”", vm.NextStepLabel);
    }

    [Fact]
    public async Task Sem_nada_para_revisar_o_proximo_passo_traduz_o_primeiro_capitulo_com_japones()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4))),
            ("yst00002", Lines("yst00002", (TranslationStatus.Untranslated, 4))));

        var vm = await Load(Dashboard(status));

        Assert.Equal("Traduzir a parte 1", vm.NextStepLabel);
    }

    [Fact]
    public async Task Tudo_pronto_com_problemas_o_proximo_passo_e_conferir_e_sem_problemas_e_testar()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4))));
        var withProblems = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(0, 2) };
        var clean = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(0, 0) };
        var tested = new ProjectDashboard(status, Stages(build: true, inject: true), AiUsageTotals.Empty) { Qa = new QaTotals(0, 0) };

        var a = await Load(withProblems);
        var b = await Load(clean);
        var c = await Load(tested);

        Assert.Equal("Conferir a tradução", a.NextStepLabel);
        Assert.Equal("Testar no jogo", b.NextStepLabel);
        Assert.Equal("Testar de novo no jogo", c.NextStepLabel);
        a.OpenNextStepCommand.Execute(null);
    }

    [Fact]
    public async Task O_botao_do_cartao_do_Inicio_e_o_proximo_passo_com_Continuar_na_frente()
    {
        var names = TwoChapters().WithGroup("yst00001", "Manhã de chuva").WithGroup("yst00002", "Manhã de chuva");

        var vm = await Load(Dashboard(Mixed(), names));
        var empty = new DashboardViewModel();

        Assert.Equal("Continuar: revisar “Manhã de chuva”", vm.ContinueLabel);
        Assert.Equal(string.Empty, empty.ContinueLabel);
    }

    [Fact]
    public async Task A_cena_do_proximo_passo_diz_o_nome_e_o_numero_do_capitulo_sem_repetir_o_que_o_nome_ja_diz()
    {
        var named = await Load(Dashboard(Mixed(), TwoChapters().WithGroup("yst00001", "Manhã de chuva").WithGroup("yst00002", "Manhã de chuva")));
        var numbered = await Load(Dashboard(Mixed(), TwoChapters()));

        Assert.Equal("Capítulo 1 · Manhã de chuva", named.NextStepScene);
        Assert.Equal("Capítulo 1", numbered.NextStepScene);
    }

    [Fact]
    public async Task Sem_capitulo_a_fazer_o_proximo_passo_nao_tem_cena()
    {
        var clean = new ProjectDashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4)))), Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(0, 0) };

        var vm = await Load(clean);

        Assert.Equal(string.Empty, vm.NextStepScene);
    }

    [Fact]
    public async Task O_passo_Conferir_diz_so_o_que_trava_o_teste_e_o_Testar_diz_quando_foi()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4))));
        var applied = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var twoErrors = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(2, 30) };
        var oneError = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(1, 0) };
        var warningsOnly = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty) { Qa = new QaTotals(0, 5) };
        var accents = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(0, 0),
            GameState = new GameStateFile(GameStateFile.CurrentVersion, new GameInGame(GameTestKind.AccentTest, applied, null, null, null), null),
        };
        var oneScene = new ProjectDashboard(status, Stages(), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(0, 0),
            GameState = new GameStateFile(GameStateFile.CurrentVersion, new GameInGame(GameTestKind.Translation, applied, null, "yst00001", null), null),
        };

        var a = await Load(twoErrors);
        var b = await Load(oneError);
        var c = await Load(warningsOnly);
        var d = await Load(accents);
        var e = await Load(oneScene);

        Assert.Equal("2 erros travam", a.Steps[3].Subtitle);
        Assert.Equal("1 erro trava", b.Steps[3].Subtitle);
        Assert.Equal("Sem erros", c.Steps[3].Subtitle);
        Assert.Equal("Ainda não testado", a.Steps[4].Subtitle);
        Assert.Equal("Ainda não testado", a.LastTestText);
        Assert.Equal("Acentos testados em 22/10", d.Steps[4].Subtitle);
        Assert.Equal("Uma cena testada em 22/10", e.Steps[4].Subtitle);
    }

    [Fact]
    public async Task As_frases_dos_passos_cabem_numa_linha_do_stepper_mesmo_com_numeros_grandes()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", (TranslationStatus.Approved, 18_802), (TranslationStatus.Machine, 11_089), (TranslationStatus.Untranslated, 18_320))));
        var applied = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var dashboard = new ProjectDashboard(status, Stages(untranslated: 18_320, awaiting: 11_089), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(2, 10_122),
            GameState = new GameStateFile(GameStateFile.CurrentVersion, new GameInGame(GameTestKind.Translation, applied, null, null, null), null),
        };

        var vm = await Load(dashboard);

        Assert.Equal(["48.211 linhas lidas", "62% traduzido", "18.802 revisadas", "2 erros travam", "Testado em 22/10"], vm.Steps.Select(s => s.Subtitle));
        Assert.All(vm.Steps, s => Assert.True(s.Subtitle.Length <= 20, s.Subtitle));
    }

    [Fact]
    public async Task Projeto_sem_scripts_nao_tem_proximo_passo_e_mostra_o_estado_vazio()
    {
        var vm = await Load(Dashboard(StatusOf()));

        Assert.False(vm.HasNextStep);
        Assert.Equal(string.Empty, vm.NextStepLabel);
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public async Task Os_passos_da_jornada_tem_estado_frase_e_levam_a_tela_certa()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4))));
        var dashboard = new ProjectDashboard(status, Stages(build: true, inject: true), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(0, 0),
            GameState = new GameStateFile(GameStateFile.CurrentVersion, new GameInGame(GameTestKind.Translation, new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero), null, null, null), null),
        };
        var vm = await Load(dashboard);
        var requested = new List<NavKey>();
        vm.NavigationRequested += (_, request) => requested.Add(request.Target);

        Assert.All(vm.Steps, s => Assert.Equal(PipelineStepState.Done, s.State));
        Assert.Equal(["4 linhas lidas", "Tudo traduzido", "Tudo revisado", "Sem erros", "Testado em 22/10"], vm.Steps.Select(s => s.Subtitle));
        Assert.Null(vm.Steps[0].Command);
        foreach (var step in vm.Steps.Skip(1))
        {
            step.Command!.Execute(step.CommandParameter);
        }

        Assert.Equal([NavKey.Editor, NavKey.Editor, NavKey.Conference, NavKey.Build], requested);
    }

    [Fact]
    public async Task O_passo_Revisar_diz_quantas_linhas_estao_revisadas()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", (TranslationStatus.Approved, 51), (TranslationStatus.Machine, 268), (TranslationStatus.Untranslated, 6535))));
        var vm = await Load(Dashboard(status));

        Assert.Equal("51 revisadas", vm.Steps[2].Subtitle);
        Assert.Equal(51, vm.ReadyLines);
        Assert.Equal(6854, vm.TotalLines);
    }

    [Fact]
    public async Task A_linha_do_capitulo_todo_traduzido_pela_IA_diz_quantas_faltam_revisar()
    {
        var vm = await Load(Dashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Machine, 200))))));
        var chapter = vm.Chapters[0];

        var row = new VNForge.App.Controls.ChapterRow { Done = chapter.Ready, Review = chapter.ToReview, Total = chapter.Total };

        Assert.Equal("200 a revisar", row.PercentText);
    }

    [Fact]
    public async Task O_passo_Revisar_sem_nada_revisado_ainda_diz_zero()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Machine, 200))));
        var vm = await Load(Dashboard(status));

        Assert.Equal("0 revisadas", vm.Steps[2].Subtitle);
    }

    [Fact]
    public async Task A_conferencia_que_nao_rodou_aparece_na_frase_do_passo()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 1))));

        var vm = await Load(new ProjectDashboard(status, Stages(), AiUsageTotals.Empty));

        Assert.Equal("Não pôde rodar", vm.Steps[3].Subtitle);
    }

    [Fact]
    public async Task O_ritmo_diz_as_horas_que_faltam_no_ritmo_da_pessoa()
    {
        var approvals = Enumerable.Range(0, 120).Select(i => new ApprovalRecord("yst00001", $"yst00001:{i:D6}", Now.AddDays(-1).AddSeconds(i * 30))).ToList();
        var dashboard = new ProjectDashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Machine, 240), (TranslationStatus.Approved, 10)))), Stages(awaiting: 240), AiUsageTotals.Empty)
        {
            Sessions = new SessionsFile(SessionsFile.CurrentVersion, approvals),
        };

        var vm = await Load(dashboard);

        Assert.Equal("No seu ritmo (120 linhas por hora), faltam cerca de 2 horas de revisão.", vm.PaceText);
    }

    [Fact]
    public async Task O_ritmo_lembra_as_linhas_de_hoje_e_fala_de_uma_hora_no_singular()
    {
        var approvals = Enumerable.Range(0, 30).Select(i => new ApprovalRecord("yst00001", $"yst00001:{i:D6}", Now.AddMinutes(-20).AddSeconds(i * 30))).ToList();
        var dashboard = new ProjectDashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Machine, 20)))), Stages(awaiting: 20), AiUsageTotals.Empty)
        {
            Sessions = new SessionsFile(SessionsFile.CurrentVersion, approvals),
        };

        var vm = await Load(dashboard);

        Assert.Equal("No seu ritmo (120 linhas por hora), faltam cerca de 1 hora de revisão. Hoje você aprovou 30 linhas.", vm.PaceText);
    }

    [Fact]
    public async Task Sem_aprovacoes_o_app_pede_para_aprovar_algumas_e_sem_nada_para_revisar_diz_isso()
    {
        var some = await Load(Dashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Machine, 3))))));
        var none = await Load(Dashboard(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 3))))));

        Assert.Equal("Aprove algumas linhas e o app calcula quantas horas de revisão faltam.", some.PaceText);
        Assert.Equal("Nenhuma linha espera revisão agora.", none.PaceText);
    }

    [Fact]
    public async Task Precisa_de_voce_mostra_no_maximo_quatro_itens_os_erros_primeiro_e_diz_quantos_ficaram_de_fora()
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(2, 5),
            Readiness = new ReadinessFacts(QaErrors: 2, QaWarnings: 5, CharactersWithoutGender: 23, NarratorDefined: false, HasStyleGuide: false, TestedInGame: true),
        };

        var vm = await Load(dashboard, new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false));

        Assert.Equal(4, vm.Todos.Count);
        Assert.Equal(["Falta a chave do Claude", "2 erros travam o teste no jogo", "23 personagens sem gênero", "Diga quem narra"], vm.Todos.Select(t => t.Title));
        Assert.Equal([SeverityLevel.Error, SeverityLevel.Error, SeverityLevel.Warning, SeverityLevel.Warning], vm.Todos.Select(t => t.Severity));
        Assert.Equal(["Colocar a chave", "Corrigir", "Definir agora", "Escolher"], vm.Todos.Select(t => t.ActionText));
        Assert.True(vm.HasMoreTodos);
        Assert.Equal("Mais 2 coisas para ver depois", vm.MoreTodosText);
    }

    [Fact]
    public async Task O_botao_de_cada_item_leva_a_tela_que_resolve()
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(), AiUsageTotals.Empty)
        {
            Readiness = new ReadinessFacts(QaErrors: 1, CharactersWithoutGender: 1, TestedInGame: true),
        };
        var vm = await Load(dashboard, new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false));
        var requested = new List<NavKey>();
        vm.NavigationRequested += (_, request) => requested.Add(request.Target);

        foreach (var todo in vm.Todos)
        {
            todo.Command!.Execute(null);
        }

        Assert.Equal([NavKey.Settings, NavKey.Conference, NavKey.Glossary], requested);
        Assert.Equal("1 erro trava o teste no jogo", vm.Todos[1].Title);
        Assert.False(vm.HasMoreTodos);
    }

    [Fact]
    public async Task Sem_nada_a_fazer_a_lista_diz_que_esta_tudo_em_ordem()
    {
        var vm = await Load(Dashboard(Mixed()));

        var todo = Assert.Single(vm.Todos);
        Assert.Equal(SeverityLevel.Ok, todo.Severity);
        Assert.Equal("Tudo em ordem", todo.Title);
        Assert.Null(todo.ActionText);
    }

    [Fact]
    public async Task O_gasto_aparece_em_reais_com_a_marca_da_cotacao_de_exemplo_e_sem_limite_convida_a_definir_um()
    {
        var usage = new AiUsageTotals(1, 1, 0, 0, 26.40m, 1);

        var vm = await Load(Dashboard(Mixed(), usage: usage));

        Assert.Equal("≈ R$ 142,56 neste projeto (cotação de exemplo)", vm.SpentText);
        Assert.Equal(0, vm.LimitBrl);
        Assert.Equal("Sem limite de gasto. Defina um nos Ajustes.", vm.SpendHint);
        Assert.DoesNotContain("US$", vm.SpentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_estado_do_jogo_segue_o_ultimo_teste_e_a_copia_de_seguranca()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 2))));
        var inGame = new GameInGame(GameTestKind.Translation, new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero), null, null, null);
        var original = await Load(new ProjectDashboard(status, Stages(), AiUsageTotals.Empty));
        var translated = await Load(new ProjectDashboard(status, Stages(), AiUsageTotals.Empty)
        {
            GameState = new GameStateFile(GameStateFile.CurrentVersion, inGame, new GameLastTest(inGame.AppliedAt, GameTestKind.Translation, [])),
        });
        var lost = await Load(new ProjectDashboard(status, Stages(), AiUsageTotals.Empty)
        {
            GameState = new GameStateFile(GameStateFile.CurrentVersion, inGame with { BackupDirectory = "2026-10-22_1200" }, null),
        });

        Assert.Equal((GameState.Original, "O jogo está como veio", "Testar no jogo"), (original.GameState, original.GameTitle, original.GameActionText));
        Assert.Equal((GameState.Translated, "O jogo está com a tradução de 22/10", "Testar de novo"), (translated.GameState, translated.GameTitle, translated.GameActionText));
        Assert.Equal(GameState.Warning, lost.GameState);
    }

    [Fact]
    public async Task Clicar_num_capitulo_abre_o_editor_na_primeira_cena_que_pede_trabalho()
    {
        var vm = await Load(Dashboard(Mixed(), TwoChapters()));
        var requested = new List<DashboardNavigation>();
        vm.NavigationRequested += (_, request) => requested.Add(request);

        vm.OpenChapterCommand.Execute(vm.Chapters[1]);
        vm.OpenChapterCommand.Execute(vm.Chapters[2]);

        Assert.Equal([new DashboardNavigation(NavKey.Editor, "yst00003"), new DashboardNavigation(NavKey.Editor, "yst00004")], requested);
    }

    [Theory]
    [InlineData("Settings", NavKey.Settings)]
    [InlineData("Glossary", NavKey.Glossary)]
    [InlineData("Conference", NavKey.Conference)]
    [InlineData("Editor", NavKey.Editor)]
    [InlineData("Test", NavKey.Build)]
    public async Task Os_links_do_Painel_pedem_a_tela_certa(string link, NavKey expected)
    {
        var vm = await Load(Dashboard(Mixed()));
        var requested = new List<NavKey>();
        vm.NavigationRequested += (_, request) => requested.Add(request.Target);

        switch (link)
        {
            case "Settings": vm.OpenSettingsCommand.Execute(null); break;
            case "Glossary": vm.OpenGlossaryCommand.Execute(null); break;
            case "Conference": vm.OpenConferenceCommand.Execute(null); break;
            case "Test": vm.OpenTestCommand.Execute(null); break;
            default: vm.OpenEditorCommand.Execute(null); break;
        }

        Assert.Equal([expected], requested);
    }

    [Fact]
    public async Task O_estado_vazio_leva_ao_Inicio()
    {
        var vm = new DashboardViewModel();
        var started = 0;
        vm.StartRequested += (_, _) => started++;
        await vm.ReloadAsync();

        vm.OpenStartCommand.Execute(null);

        Assert.Equal(1, started);
        Assert.Equal("Abrir projeto", vm.EmptyActionText);
    }

    private static ProjectDashboard AllApproved() => new(StatusOf(("yst00001", Lines("yst00001", (TranslationStatus.Approved, 4)))), Stages(), AiUsageTotals.Empty)
    {
        Qa = new QaTotals(0, 0),
        Readiness = new ReadinessFacts(TestedInGame: false),
    };

    [Fact]
    public async Task Na_cena_de_exemplo_sem_chave_o_Painel_nao_cobra_chave_nem_teste_no_jogo()
    {
        var noKey = new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false);

        var vm = await Load(AllApproved(), noKey, Summary with { IsSample = true });

        Assert.DoesNotContain(vm.Todos, t => t.Title == "Falta a chave do Claude");
        Assert.DoesNotContain(vm.Todos, t => t.Title == "Teste os acentos no jogo");
        Assert.False(vm.HasNextStep);
        Assert.Empty(vm.NextStepLabel);
        Assert.Equal("Sem jogo na cena de exemplo", vm.LastTestText);
    }

    [Fact]
    public async Task Um_projeto_real_sem_chave_continua_com_o_aviso_e_o_passo_de_testar_no_jogo()
    {
        var noKey = new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false);

        var vm = await Load(AllApproved(), noKey);

        Assert.Contains(vm.Todos, t => t.Title == "Falta a chave do Claude");
        Assert.True(vm.HasNextStep);
        Assert.Equal(NavKey.Build, vm.NextStepTarget!.Target);
    }

    [Fact]
    public async Task O_detalhe_do_Inicio_de_um_projeto_real_mostra_o_aviso_de_chave_com_a_cena_de_exemplo_aberta()
    {
        var noKey = new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false);
        var panel = await Load(AllApproved(), noKey, Summary with { IsSample = true });
        var detail = await Load(AllApproved(), noKey);

        Assert.DoesNotContain(panel.Todos, t => t.Title == "Falta a chave do Claude");
        Assert.Contains(detail.Todos, t => t.Title == "Falta a chave do Claude");
    }
}
