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
    public async Task O_detalhe_do_Inicio_de_um_projeto_real_mostra_o_aviso_de_chave_com_a_cena_de_exemplo_aberta()
    {
        var noKey = new DashboardContext(ExchangeRate.Placeholder, BudgetLimits.None, HasApiKey: false);
        var panel = await Load(AllApproved(), noKey, Summary with { IsSample = true });
        var detail = await Load(AllApproved(), noKey);

        Assert.DoesNotContain(panel.Todos, t => t.Title == "Falta a chave do Claude");
        Assert.Contains(detail.Todos, t => t.Title == "Falta a chave do Claude");
    }
}
