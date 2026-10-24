using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ConferenceViewModelTests
{
    private static TextUnit Unit(string key, int n, string? translation, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", translation, status);

    /// <summary>Dois scripts: espaço duplo (aviso, corrigível) em duas linhas, uma linha aprovada sem texto (erro) e uma pendente (aviso).</summary>
    private static (ConferenceViewModel Vm, ProjectFolder Folder) Open(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Unit("yst00001", 0, "olá  mundo", TranslationStatus.Translated),
            Unit("yst00001", 1, "tudo certo", TranslationStatus.Translated),
            Unit("yst00001", 2, null, TranslationStatus.Approved),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
        ]);
        folder.SaveText("yst00002", [Unit("yst00002", 5, "a  b  c", TranslationStatus.Reviewed)]);
        return (new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) }, folder);
    }

    private static async Task<(ConferenceViewModel Vm, ProjectFolder Folder)> Loaded(TempFolder temp)
    {
        var opened = Open(temp);
        await opened.Vm.RefreshAsync();
        return opened;
    }

    private static IEnumerable<ConferenceIssueRowViewModel> IssueRows(ConferenceViewModel vm) => vm.Rows.OfType<ConferenceIssueRowViewModel>();

    [Fact]
    public async Task Sem_projeto_a_lista_fica_vazia_e_nao_quebra()
    {
        var vm = new ConferenceViewModel();

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.Equal("Abra um projeto para conferir as traduções.", vm.EmptyMessage);
    }

    [Fact]
    public async Task A_lista_bate_com_o_vnf_check_na_ordem_e_nos_textos()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        var expected = QaEngine.Default.Run(QaContext.FromProject(folder)).Issues;

        Assert.Equal(expected.Select(i => (i.RuleId, i.UnitId, i.Message)), IssueRows(vm).Select(r => (r.RuleId, r.UnitId, r.Message)));
        Assert.Equal(QaBlockedException.Describe(QaEngine.Default.Run(QaContext.FromProject(folder))), vm.Summary);
    }

    [Fact]
    public async Task Filtro_de_erros_bate_com_vnf_check_severity_error()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        vm.SelectSeverityCommand.Execute(vm.SeverityFilters.Single(f => f.Filter == ConferenceSeverityFilter.Errors));

        var expected = QaEngine.Default.Run(QaContext.FromProject(folder), new QaOptions(null, QaSeverity.Error)).Issues;
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(i => (i.RuleId, i.UnitId)), IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.All(IssueRows(vm), r => Assert.True(r.IsError));
    }

    [Fact]
    public async Task Filtro_de_avisos_mostra_so_avisos_e_os_chips_contam_cada_gravidade()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal([5, 1, 4], vm.SeverityFilters.Select(f => f.Count));

        vm.SelectSeverityCommand.Execute(vm.SeverityFilters.Single(f => f.Filter == ConferenceSeverityFilter.Warnings));

        Assert.Equal(4, IssueRows(vm).Count());
        Assert.All(IssueRows(vm), r => Assert.False(r.IsError));
        Assert.Equal([false, false, true], vm.SeverityFilters.Select(f => f.IsSelected));
    }

    [Fact]
    public async Task Problemas_vem_agrupados_por_regra_com_cabecalho_e_contagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var groups = vm.Rows.OfType<ConferenceGroupRowViewModel>().ToList();

        Assert.Equal(["nao-traduzida", "traducao-vazia", "espaco-duplo"], groups.Select(g => g.RuleId));
        var spaces = groups.Single(g => g.RuleId == "espaco-duplo");
        Assert.Equal(3, spaces.Count);
        Assert.True(spaces.CanFix);
        Assert.Equal("Dois ou mais espaços seguidos na tradução.", spaces.Description);
        Assert.False(groups.Single(g => g.RuleId == "traducao-vazia").CanFix);

        // Cada cabeçalho precede só as ocorrências da própria regra.
        string? current = null;
        foreach (var row in vm.Rows)
        {
            if (row is ConferenceGroupRowViewModel group)
            {
                current = group.RuleId;
            }
            else
            {
                Assert.Equal(current, ((ConferenceIssueRowViewModel)row).RuleId);
            }
        }
    }

    [Fact]
    public async Task Clicar_na_ocorrencia_pede_a_linha_do_script_certo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        LineRequest? received = null;
        vm.LineRequested += (_, request) => received = request;
        var row = IssueRows(vm).First(r => r.UnitId == "yst00002:000005");

        vm.OpenLineCommand.Execute(row);

        Assert.Equal(new LineRequest("yst00002", "yst00002:000005"), received);
    }

    [Fact]
    public async Task Marcar_como_ok_grava_o_flag_e_tira_o_aviso_da_lista()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var row = IssueRows(vm).First(r => r.UnitId == "yst00001:000000");

        vm.MarkOkCommand.Execute(row);

        Assert.Contains("espaco-duplo", folder.LoadText("yst00001").Single(u => u.Id == "yst00001:000000").Flags);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000");
        Assert.Equal(1, changed);
        Assert.Contains("1 aviso marcado como ok", vm.SilencedSummary);

        // O que a tela mostra continua igual ao vnf check depois da marca.
        var expected = QaEngine.Default.Run(QaContext.FromProject(folder));
        Assert.Equal(expected.Issues.Select(i => (i.RuleId, i.UnitId)), IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.Single(expected.Silenced);
    }

    [Fact]
    public async Task Erro_nao_pode_ser_marcado_como_ok()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var row = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");

        Assert.False(row.CanMarkOk);
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.Empty(folder.LoadText("yst00001").SelectMany(u => u.Flags));
    }

    [Fact]
    public async Task Corrigir_a_regra_aplica_so_o_fixer_dela_grava_e_reconfere()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var group = vm.Rows.OfType<ConferenceGroupRowViewModel>().Single(g => g.RuleId == "espaco-duplo");

        await vm.FixRuleCommand.ExecuteAsync(group);

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a b c", folder.LoadText("yst00002")[0].Translation);
        Assert.DoesNotContain(vm.Rows.OfType<ConferenceGroupRowViewModel>(), g => g.RuleId == "espaco-duplo");
        Assert.Equal("2 correções aplicadas.", vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.Equal(QaBlockedException.Describe(QaEngine.Default.Run(QaContext.FromProject(folder))), vm.Summary);
    }

    [Fact]
    public async Task Corrigir_tudo_roda_todos_os_fixers_e_avisa_quando_nao_ha_o_que_corrigir()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        await vm.FixAllCommand.ExecuteAsync(null);
        Assert.Equal("2 correções aplicadas.", vm.NoticeMessage);
        Assert.DoesNotContain(QaEngine.Default.Run(QaContext.FromProject(folder)).Issues, i => i.RuleId == "espaco-duplo");

        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal("Nada a corrigir automaticamente.", vm.NoticeMessage);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Corrigir_respeita_o_ok_marcado_pelo_revisor()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        vm.MarkOkCommand.Execute(IssueRows(vm).First(r => r.UnitId == "yst00001:000000"));

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.Equal("olá  mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a b c", folder.LoadText("yst00002")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Projeto_sem_problemas_mostra_a_mensagem_de_tudo_certo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Nenhum problema encontrado.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Projeto_ilegivel_mostra_o_erro_e_a_lista_fica_vazia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        File.WriteAllText(folder.TextPath("yst00001"), "{ isto não é json");

        await vm.RefreshAsync();

        Assert.Empty(vm.Rows);
        Assert.StartsWith("Não foi possível conferir o projeto:", vm.ErrorMessage);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_lista_anterior()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        Assert.NotEmpty(vm.Rows);

        vm.Project = null;

        Assert.Empty(vm.Rows);
        Assert.Equal("Abra um projeto para conferir as traduções.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Enquanto_confere_fica_ocupada_e_nenhum_comando_grava()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        using var gate = new ManualResetEventSlim();
        var vm = new ConferenceViewModel(new QaEngine([new GateRule(gate)])) { Project = ProjectSummaryReader.Read(root) };
        var row = new ConferenceIssueRowViewModel(new QaIssue("regra-porta", QaSeverity.Warning, "yst00001:000000", "yst00001", "problema"));

        var running = vm.RefreshAsync();

        Assert.True(vm.IsBusy);
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.False(vm.OpenLineCommand.CanExecute(row));

        gate.Set();
        await running;

        Assert.False(vm.IsBusy);
        Assert.True(vm.FixAllCommand.CanExecute(null));
        Assert.True(vm.MarkOkCommand.CanExecute(row));
    }

    private sealed class GateRule(ManualResetEventSlim gate) : IQaRule
    {
        public string Id => "regra-porta";

        public string Description => "Espera o teste liberar.";

        public IEnumerable<QaIssue> Check(QaContext context)
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        }
    }
}
