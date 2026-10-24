using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os dados que o Painel do design v2 mostra além dos números básicos: cabeçalho, resumo das etapas, os três cartões e as duas tabelas.</summary>
public class DashboardViewModelDataTests
{
    private const string Root = @"D:\Traducoes\minha-vn-ptbr";

    private static readonly ProjectSummary Summary = new("Minha VN — PT-BR", Root, "yuris", 500, "cp1252", "claude-opus-5-5", 3, new StatusCounts(0, 0, 0, 0, 0));

    private static List<TextUnit> Lines(string key, string? speaker, params (TranslationStatus Status, int Count)[] groups) =>
    [
        .. groups.SelectMany(g => Enumerable.Repeat(g.Status, g.Count))
            .Select((status, i) => new TextUnit($"{key}:{i:D6}", TextUnitKind.Dialogue, speaker, $"原文{i}", status == TranslationStatus.Untranslated ? null : "tradução", status)),
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

    private static async Task<DashboardViewModel> Load(ProjectDashboard dashboard, Action<string>? folderOpener = null)
    {
        var vm = new DashboardViewModel(_ => dashboard, folderOpener) { Project = Summary };
        await vm.ReloadAsync();
        return vm;
    }

    private static ProjectDashboard Dashboard(ProjectStatus status, IReadOnlyList<PipelineStage>? stages = null, AiUsageTotals? usage = null) =>
        new(status, stages ?? Stages(untranslated: status.Total.Untranslated), usage ?? AiUsageTotals.Empty);

    private static ProjectStatus Mixed() => StatusOf(
        ("yst00001", Lines("yst00001", "美咲", (TranslationStatus.Approved, 21), (TranslationStatus.Reviewed, 18), (TranslationStatus.Translated, 6))),
        ("yst00002", Lines("yst00002", null, (TranslationStatus.Machine, 17), (TranslationStatus.Untranslated, 38))));

    [Fact]
    public async Task Cabecalho_traz_a_pasta_com_a_engine_e_o_nome_do_projeto()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal(@"D:\Traducoes\minha-vn-ptbr · YU-RIS · YPF v500", vm.Breadcrumb);
        Assert.Equal("Minha VN — PT-BR", vm.Title);
    }

    [Fact]
    public async Task Abrir_pasta_pede_a_pasta_do_projeto_ao_sistema()
    {
        var opened = new List<string>();
        var vm = await Load(Dashboard(Mixed()), opened.Add);

        vm.OpenFolderCommand.Execute(null);

        Assert.Equal([Root], opened);
    }

    [Fact]
    public async Task Continuar_vai_ao_primeiro_script_com_linhas_pendentes()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", null, (TranslationStatus.Approved, 3))),
            ("yst00002", Lines("yst00002", null, (TranslationStatus.Machine, 2))),
            ("yst00003", Lines("yst00003", null, (TranslationStatus.Approved, 1), (TranslationStatus.Untranslated, 1))));
        var vm = await Load(Dashboard(status));
        var requested = new List<DashboardNavigation>();
        vm.NavigationRequested += (_, request) => requested.Add(request);

        vm.ContinueCommand.Execute(null);

        Assert.Equal("Continuar em yst00003", vm.ContinueLabel);
        Assert.True(vm.CanContinue);
        Assert.Equal([new DashboardNavigation(NavKey.Editor, "yst00003")], requested);
    }

    [Fact]
    public async Task Sem_pendentes_Continuar_vai_ao_primeiro_script_com_linhas_para_conferir()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", null, (TranslationStatus.Approved, 3))),
            ("yst00002", Lines("yst00002", null, (TranslationStatus.Machine, 2))));

        var vm = await Load(Dashboard(status));

        Assert.Equal("Continuar em yst00002", vm.ContinueLabel);
    }

    [Fact]
    public async Task Tudo_aprovado_Continuar_vai_ao_primeiro_script()
    {
        var status = StatusOf(
            ("yst00005", Lines("yst00005", null, (TranslationStatus.Approved, 3))),
            ("yst00006", Lines("yst00006", null, (TranslationStatus.Approved, 3))));

        Assert.Equal("Continuar em yst00005", (await Load(Dashboard(status))).ContinueLabel);
    }

    [Fact]
    public async Task Projeto_sem_scripts_nao_tem_Continuar()
    {
        var vm = await Load(Dashboard(StatusOf()));
        var requested = new List<DashboardNavigation>();
        vm.NavigationRequested += (_, request) => requested.Add(request);

        vm.ContinueCommand.Execute(null);

        Assert.False(vm.CanContinue);
        Assert.Equal(string.Empty, vm.ContinueLabel);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task As_etapas_ligadas_ao_stepper_tem_estado_resumo_e_comando()
    {
        var qa = new QaTotals(2, 37);
        var dashboard = new ProjectDashboard(Mixed(), Stages(untranslated: 38, awaiting: 23), AiUsageTotals.Empty)
        {
            Qa = qa,
            Build = BuildStatus.Stale,
            LastBackupAt = new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.FromHours(-3)),
        };

        var vm = await Load(dashboard);

        Assert.Equal(["Extrair", "Traduzir", "Conferir", "Build", "Injetar"], vm.Steps.Select(s => s.Title));
        Assert.Equal([PipelineStepState.Done, PipelineStepState.Active, PipelineStepState.Pending, PipelineStepState.Pending, PipelineStepState.Pending], vm.Steps.Select(s => s.State));
        Assert.Equal(
            ["100 linhas em 2 scripts", "62% · 38 pendentes", "2 erros · 37 avisos", "Os textos mudaram desde o último", "backup de 22/10 pronto"],
            vm.Steps.Select(s => s.Subtitle));
        Assert.All(vm.Steps, s => Assert.Same(vm.OpenStageCommand, s.Command));
        Assert.Equal([1, 2, 3, 4, 5], vm.Steps.Select(s => s.Number));
    }

    [Fact]
    public async Task Resumos_das_etapas_quando_tudo_esta_pronto()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", null, (TranslationStatus.Approved, 2))));
        var dashboard = new ProjectDashboard(status, Stages(build: true, inject: true), AiUsageTotals.Empty)
        {
            Qa = new QaTotals(0, 0),
            Build = BuildStatus.Current,
            LastBackupAt = new DateTimeOffset(2026, 10, 22, 14, 0, 0, TimeSpan.Zero),
        };

        var vm = await Load(dashboard);

        Assert.All(vm.Steps, s => Assert.Equal(PipelineStepState.Done, s.State));
        Assert.Equal(
            ["2 linhas em 1 script", "100% · nada pendente", "Nenhum problema", "Build em dia", "Injetado · backup de 22/10"],
            vm.Steps.Select(s => s.Subtitle));
    }

    [Fact]
    public async Task Resumos_das_etapas_sem_nada_feito()
    {
        var status = StatusOf(("yst00001", Lines("yst00001", null, (TranslationStatus.Untranslated, 1))));
        var dashboard = new ProjectDashboard(status, Stages(extract: false, untranslated: 1), AiUsageTotals.Empty) { Qa = new QaTotals(1, 1) };

        var vm = await Load(dashboard);

        Assert.Equal(
            ["Ainda não extraído", "0% · 1 pendente", "1 erro · 1 aviso", "Nenhum build ainda", "Nenhuma injeção ainda"],
            vm.Steps.Select(s => s.Subtitle));
        Assert.Equal(PipelineStepState.Active, vm.Steps[0].State);
    }

    [Fact]
    public async Task Conferencia_indisponivel_aparece_no_resumo_da_etapa()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal("Conferência indisponível", vm.Steps[2].Subtitle);
    }

    [Fact]
    public async Task Cartao_de_linhas_traz_o_total_o_percentual_concluido_e_as_pendentes()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal("100", vm.LinesLabel);
        Assert.Equal("39% concluídas · 38 pendentes", vm.LinesDescription);
        Assert.Equal(new StatusDistribution(21, 18, 6, 17, 38), vm.Distribution);
    }

    [Fact]
    public async Task Cartao_de_IA_mostra_gasto_tokens_modelo_e_estimativa_do_restante()
    {
        var usage = new AiUsageTotals(2_100_000, 900_000, 0, 0, 26.40m, 3);

        var vm = await Load(Dashboard(Mixed(), usage: usage));

        Assert.Equal("US$ 26,40", vm.AiValue);
        Assert.Equal("2,1 M tokens de entrada · 900 mil de saída · Claude Opus 5.5", vm.AiDescription);
        // 62 linhas fora de pendente custaram US$ 26,40; as 38 pendentes saem a US$ 16,18.
        Assert.Equal(["Gasto US$ 26,40", "Restante estimado US$ 16,18"], vm.AiLegend.Select(l => l.Text));
        Assert.Equal([LegendTone.Machine, LegendTone.Pending], vm.AiLegend.Select(l => l.Tone));
        Assert.Equal(new StatusDistribution(0, 0, 0, 62, 38), vm.AiDistribution);
        Assert.Equal("Gasto US$ 26,40 de US$ 42,58 estimados, 62%", vm.AiBarDescription);
    }

    [Fact]
    public async Task Entrada_de_cache_conta_como_tokens_de_entrada()
    {
        var usage = new AiUsageTotals(1_000_000, 500_000, 800_000, 300_000, 1m, 1);

        var vm = await Load(Dashboard(Mixed(), usage: usage));

        Assert.StartsWith("2,1 M tokens de entrada · 500 mil de saída", vm.AiDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_uso_de_IA_o_cartao_nao_estima_nada()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal("US$ 0,00", vm.AiValue);
        Assert.Equal("Nenhuma chamada de IA registrada · Claude Opus 5.5", vm.AiDescription);
        Assert.Equal(["Gasto US$ 0,00"], vm.AiLegend.Select(l => l.Text));
        Assert.Equal(100, vm.AiDistribution.Pending);
    }

    [Fact]
    public async Task Execucao_sem_preco_avisa_que_o_custo_e_parcial_e_nao_estima()
    {
        var parcial = await Load(Dashboard(Mixed(), usage: new AiUsageTotals(20, 10, 0, 0, 1m, 2, UnpricedRuns: 1)));
        var desconhecido = await Load(Dashboard(Mixed(), usage: new AiUsageTotals(10, 5, 0, 0, 0m, 1, UnpricedRuns: 1)));

        Assert.EndsWith("· custo parcial (1 execução sem preço)", parcial.AiDescription, StringComparison.Ordinal);
        Assert.Single(parcial.AiLegend);
        Assert.EndsWith("· custo desconhecido (modelo sem preço na tabela)", desconhecido.AiDescription, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(26.40, 62, 38, 16.18)]
    [InlineData(10, 0, 5, null)]
    [InlineData(0, 10, 5, null)]
    [InlineData(10, 10, 0, 0.0)]
    public void Estimativa_do_restante_usa_o_custo_medio_por_linha_ja_traduzida(double dollars, int done, int pending, double? expected)
    {
        var counts = new StatusCounts(pending, done, 0, 0, 0);

        var estimate = DashboardViewModel.EstimateRemaining(new AiUsageTotals(1, 1, 0, 0, (decimal)dollars, 1), counts);

        Assert.Equal((decimal?)expected, estimate);
    }

    [Fact]
    public async Task Cartao_de_conferencia_traz_problemas_erros_e_avisos()
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(untranslated: 38), AiUsageTotals.Empty) { Qa = new QaTotals(2, 37) };

        var vm = await Load(dashboard);

        Assert.Equal("39 problemas", vm.ConferenceValue);
        Assert.Equal("Os erros bloqueiam o build", vm.ConferenceDescription);
        Assert.Equal((2, 37), (vm.ConferenceErrors, vm.ConferenceWarnings));
        Assert.Equal(["Erros 2", "Avisos 37"], vm.ConferenceLegend.Select(l => l.Text));
        Assert.Equal([LegendTone.Error, LegendTone.Warning], vm.ConferenceLegend.Select(l => l.Tone));
        Assert.Equal("2 erros e 37 avisos", vm.ConferenceBarDescription);
    }

    [Theory]
    [InlineData(0, 0, "Nenhum problema", "Nada a corrigir")]
    [InlineData(0, 1, "1 problema", "Revise os avisos antes do build")]
    [InlineData(1, 0, "1 problema", "Os erros bloqueiam o build")]
    public async Task Conferencia_descreve_os_casos_sem_erro_ou_sem_aviso(int errors, int warnings, string value, string description)
    {
        var dashboard = new ProjectDashboard(Mixed(), Stages(untranslated: 38), AiUsageTotals.Empty) { Qa = new QaTotals(errors, warnings) };

        var vm = await Load(dashboard);

        Assert.Equal(value, vm.ConferenceValue);
        Assert.Equal(description, vm.ConferenceDescription);
    }

    [Fact]
    public async Task Conferencia_que_nao_rodou_mostra_travessao_e_a_razao()
    {
        var vm = await Load(Dashboard(Mixed()));

        Assert.Equal("—", vm.ConferenceValue);
        Assert.Equal("Não foi possível conferir o projeto", vm.ConferenceDescription);
        Assert.Empty(vm.ConferenceLegend);
    }

    [Fact]
    public async Task Tabela_de_scripts_mostra_id_nome_linhas_e_percentual_concluido()
    {
        var names = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { ["yst00001"] = "prologo" }).Rename("yst00001", "Prólogo");
        var dashboard = new ProjectDashboard(Mixed(), Stages(untranslated: 38), AiUsageTotals.Empty) { ScriptNames = names };

        var vm = await Load(dashboard);

        var first = vm.Scripts[0];
        Assert.Equal(("yst00001", "Prólogo", "45", "87%"), (first.Key, first.Name, first.LinesLabel, first.PercentLabel));
        Assert.Equal(new StatusDistribution(47, 40, 13, 0, 0), first.Distribution);
        var second = vm.Scripts[1];
        Assert.Equal(("yst00002", string.Empty, "55", "0%"), (second.Key, second.Name, second.LinesLabel, second.PercentLabel));
        Assert.Equal("2 de 2 scripts", vm.ScriptsFooter);
    }

    [Fact]
    public async Task Tabela_de_scripts_poe_os_nomeados_antes_e_mostra_so_os_oito_primeiros()
    {
        var scripts = Enumerable.Range(1, 12)
            .Select(n => ($"yst{n:D5}", Lines($"yst{n:D5}", null, (TranslationStatus.Approved, n))))
            .ToArray();
        var names = ScriptNames.Empty.Rename("yst00010", "Dez").Rename("yst00011", "Onze");
        var dashboard = new ProjectDashboard(StatusOf(scripts), Stages(), AiUsageTotals.Empty) { ScriptNames = names };

        var vm = await Load(dashboard);

        Assert.Equal(
            ["yst00010", "yst00011", "yst00001", "yst00002", "yst00003", "yst00004", "yst00005", "yst00006"],
            vm.Scripts.Select(s => s.Key));
        Assert.Equal("8 de 12 scripts", vm.ScriptsFooter);
    }

    [Fact]
    public async Task Tabela_de_personagens_mostra_original_traducao_e_a_narracao_em_ultimo_lugar()
    {
        var status = StatusOf(
            ("yst00001", Lines("yst00001", "美咲", (TranslationStatus.Approved, 3), (TranslationStatus.Machine, 1))),
            ("yst00002", Lines("yst00002", "？？？", (TranslationStatus.Reviewed, 2))),
            ("yst00003", Lines("yst00003", null, (TranslationStatus.Untranslated, 5))));
        var dashboard = new ProjectDashboard(status, Stages(untranslated: 5), AiUsageTotals.Empty)
        {
            CharacterNames = new Dictionary<string, string> { ["美咲"] = "Misaki" },
        };

        var vm = await Load(dashboard);

        var misaki = vm.Characters[0];
        Assert.Equal(("美咲", "Misaki", true, "4", "75%"), (misaki.Source, misaki.Name, misaki.HasSource, misaki.LinesLabel, misaki.PercentLabel));
        var unknown = vm.Characters[1];
        Assert.Equal((null, "？？？", false, false), (unknown.Source, unknown.Name, unknown.HasSource, unknown.IsNarration));
        var narration = vm.Characters[2];
        Assert.Equal(("Narração", true, "0%"), (narration.Name, narration.IsNarration, narration.PercentLabel));
        Assert.Equal("3 de 3 personagens", vm.CharactersFooter);
    }

    [Fact]
    public async Task Tabela_de_personagens_mostra_so_os_oito_primeiros()
    {
        var scripts = Enumerable.Range(1, 10).Select(n => ($"yst{n:D5}", Lines($"yst{n:D5}", $"人{n}", (TranslationStatus.Approved, 20 - n)))).ToArray();
        var dashboard = new ProjectDashboard(StatusOf(scripts), Stages(), AiUsageTotals.Empty);

        var vm = await Load(dashboard);

        Assert.Equal(8, vm.Characters.Count);
        Assert.Equal("8 de 10 personagens", vm.CharactersFooter);
    }

    [Theory]
    [InlineData(NavKey.Editor)]
    [InlineData(NavKey.Settings)]
    [InlineData(NavKey.Conference)]
    [InlineData(NavKey.Glossary)]
    public async Task Os_links_dos_cartoes_e_das_tabelas_pedem_a_tela_certa(NavKey expected)
    {
        var vm = await Load(Dashboard(Mixed()));
        var requested = new List<DashboardNavigation>();
        vm.NavigationRequested += (_, request) => requested.Add(request);

        System.Windows.Input.ICommand command = expected switch
        {
            NavKey.Editor => vm.OpenEditorCommand,
            NavKey.Settings => vm.OpenSettingsCommand,
            NavKey.Conference => vm.OpenConferenceCommand,
            _ => vm.OpenGlossaryCommand,
        };
        command.Execute(null);

        Assert.Equal([new DashboardNavigation(expected)], requested);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 5, 50)]
    [InlineData(10, 9, 90)]
    [InlineData(10, 10, 100)]
    [InlineData(200, 199, 99)]
    [InlineData(3, 1, 33)]
    [InlineData(3, 2, 67)]
    [InlineData(48_211, 18_802, 39)]
    public void Percentual_concluido_arredonda_e_so_chega_a_100_com_tudo_concluido(int total, int completed, int expected)
    {
        var counts = new StatusCounts(total - completed, 0, 0, 0, completed);

        Assert.Equal(expected, DashboardViewModel.CompletedPercent(counts));
    }
}
