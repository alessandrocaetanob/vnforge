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
    public async Task Filtro_de_erros_bate_com_vnf_check_severity_error()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);

        vm.SelectSeverityCommand.Execute(vm.SeverityFilters.Single(f => f.Filter == ConferenceSeverityFilter.Errors));

        var expected = QaEngine.Default.Run(QaContext.FromProject(folder), new QaOptions(null, QaSeverity.Error)).Issues;
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(i => (i.RuleId, i.UnitId)), IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.All(IssueRows(vm), r => Assert.True(r.IsError));
        Assert.Equal(expected.Count, IssueRows(vm).Sum(r => r.Count));
    }

    [Fact]
    public async Task Filtro_de_avisos_mostra_so_avisos_e_os_chips_contam_cada_gravidade()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal([5, 1, 4], vm.SeverityFilters.Select(f => f.Count));

        vm.SelectSeverityCommand.Execute(vm.SeverityFilters.Single(f => f.Filter == ConferenceSeverityFilter.Warnings));

        // Quatro avisos no relatório: dois espaços duplos na mesma linha viram uma linha da lista, e a "não traduzida" é o resumo do script.
        Assert.Equal(3, IssueRows(vm).Count());
        Assert.Equal(4, IssueRows(vm).Sum(r => r.Count));
        Assert.All(IssueRows(vm), r => Assert.False(r.IsError));
        Assert.Equal([false, false, true], vm.SeverityFilters.Select(f => f.IsSelected));
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
        var row = IssueRows(vm).First(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");

        await vm.MarkOkCommand.ExecuteAsync(row);

        Assert.Contains("espaco-duplo", folder.LoadText("yst00001").Single(u => u.Id == "yst00001:000000").Flags);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.Equal(1, changed);
        Assert.Contains("1 aviso marcado como ok", vm.SilencedSummary);

        // O que a tela mostra continua igual ao vnf check depois da marca: as mesmas ocorrências, sem as silenciadas.
        var expected = QaEngine.Default.Run(QaContext.FromProject(folder));
        Assert.Equal(expected.Issues.Count, IssueRows(vm).Sum(r => r.Count));
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
        await vm.MarkOkCommand.ExecuteAsync(IssueRows(vm).First(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo"));

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

    [Fact]
    public async Task Enquanto_corrige_fica_ocupada_e_nem_conferir_de_novo_nem_ok_gravam()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var vm = new ConferenceViewModel(new QaEngine([new FixGateRule(entered, gate)])) { Project = ProjectSummaryReader.Read(root) };
        var row = new ConferenceIssueRowViewModel(new QaIssue("regra-porta", QaSeverity.Warning, "yst00001:000000", "yst00001", "problema"));

        var running = vm.FixAllCommand.ExecuteAsync(null);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(vm.IsFixing);
        Assert.True(vm.IsBusy);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.FixAllCommand.CanExecute(null));
        Assert.False(vm.MarkOkCommand.CanExecute(row));
        Assert.False(vm.OpenLineCommand.CanExecute(row));

        gate.Set();
        await running;

        Assert.False(vm.IsFixing);
        Assert.False(vm.IsBusy);
        Assert.True(vm.RefreshCommand.CanExecute(null));
        Assert.Equal("Nada a corrigir automaticamente.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Regra_que_lanca_vira_mensagem_e_nao_deixa_a_pagina_ocupada()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new ConferenceViewModel(new QaEngine([new ThrowingRule()])) { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Rows);
        Assert.StartsWith("Não foi possível conferir o projeto: regra quebrada.", vm.ErrorMessage);
        Assert.True(vm.FixAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Reconferencia_que_lanca_depois_de_corrigir_libera_a_pagina_e_mostra_o_erro()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new ConferenceViewModel(new QaEngine([new ThrowingRule()])) { Project = ProjectSummaryReader.Read(root) };

        await vm.FixAllCommand.ExecuteAsync(null);

        Assert.False(vm.IsBusy);
        Assert.StartsWith("Não foi possível conferir o projeto:", vm.ErrorMessage);
        Assert.Null(vm.NoticeMessage);
    }

    [Fact]
    public async Task Falha_no_segundo_script_mantem_o_primeiro_corrigido_avisa_e_reconfere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O bloqueio de arquivo por FileShare só vale no Windows.");
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;

        // Leitura compartilhada deixa o Core ler, mas impede o File.Move que regrava o segundo script.
        using (new FileStream(folder.TextPath("yst00002"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await vm.FixAllCommand.ExecuteAsync(null);
        }

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a  b  c", folder.LoadText("yst00002")[0].Translation);
        Assert.StartsWith("Não foi possível corrigir automaticamente:", vm.ErrorMessage);
        Assert.EndsWith("continuam gravados.", vm.ErrorMessage);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.False(vm.IsBusy);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.Contains(IssueRows(vm), r => r.UnitId == "yst00002:000005" && r.RuleId == "espaco-duplo");
    }

    [Fact]
    public async Task Marcar_como_ok_em_linha_que_sumiu_mostra_o_erro_e_mantem_o_aviso()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var before = IssueRows(vm).Count();
        var missing = new ConferenceIssueRowViewModel(new QaIssue("espaco-duplo", QaSeverity.Warning, "yst00001:000099", "yst00001", "x"));

        await vm.MarkOkCommand.ExecuteAsync(missing);

        Assert.StartsWith("Não foi possível marcar como ok:", vm.ErrorMessage);
        Assert.EndsWith("O aviso continua na lista.", vm.ErrorMessage);
        Assert.Equal(before, IssueRows(vm).Count());
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_lista_poe_erros_primeiro_depois_por_script_e_linha_e_a_nao_traduzida_por_ultimo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal(
            [("traducao-vazia", "yst00001:000002"), ("espaco-duplo", "yst00001:000000"), ("espaco-duplo", "yst00002:000005"), ("nao-traduzida", "yst00001:000003")],
            IssueRows(vm).Select(r => (r.RuleId, r.UnitId)));
        Assert.Equal(["Erro", "Aviso", "Aviso", "Aviso"], IssueRows(vm).Select(r => r.SeverityLabel));
    }

    [Fact]
    public async Task Nao_traduzida_vira_uma_linha_por_script_com_a_contagem()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [Unit("yst00001", 0, null, TranslationStatus.Untranslated), Unit("yst00001", 1, null, TranslationStatus.Untranslated), Unit("yst00001", 2, "ok", TranslationStatus.Approved)]);
        folder.SaveText("yst00002", [Unit("yst00002", 0, null, TranslationStatus.Untranslated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };

        await vm.RefreshAsync();

        var rows = IssueRows(vm).ToList();
        Assert.Equal(["yst00001", "yst00002"], rows.Select(r => r.LineLabel));
        Assert.Equal([2, 1], rows.Select(r => r.Count));
        Assert.Equal("Não traduzida", rows[0].Title);
        Assert.Equal("2 linhas sem tradução neste script.", rows[0].Detail);
        Assert.Equal("1 linha sem tradução neste script.", rows[1].Detail);
        Assert.All(rows, r => Assert.False(r.CanMarkOk));
        Assert.Equal("3", vm.SeverityFilters[0].CountLabel);
        Assert.Equal("Mostrando 3 de 3 problemas abertos · agrupados por script", vm.ListFooter);

        // Abrir no editor leva à primeira linha sem tradução do script.
        LineRequest? received = null;
        vm.LineRequested += (_, request) => received = request;
        vm.OpenLineCommand.Execute(rows[0]);
        Assert.Equal(new LineRequest("yst00001", "yst00001:000000"), received);
    }

    [Fact]
    public async Task A_coluna_de_regras_conta_por_categoria_e_marca_a_pior_gravidade()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal(
            ["Todas", "Marcadores", "Caixa de texto", "Espaços", "Pontuação", "Acentuação", "Glossário", "Consistência", "Tradução"],
            vm.Categories.Where(c => c.IsVisible).Select(c => c.Label));
        Assert.Equal([5, 0, 0, 3, 0, 0, 0, 0, 2], vm.Categories.Where(c => c.IsVisible).Select(c => c.Count));

        var spaces = vm.Categories.Single(c => c.Category == QaCategories.Spaces);
        Assert.True(spaces.HasWarnings);
        Assert.False(spaces.HasErrors);
        var translation = vm.Categories.Single(c => c.Category == QaCategories.Translation);
        Assert.True(translation.HasErrors);
        var markers = vm.Categories.Single(c => c.Category == QaCategories.Markers);
        Assert.True(markers.IsClear);
        Assert.False(vm.Categories[0].IsClear);
        Assert.False(vm.Categories.Single(c => c.Category == QaCategories.Other).IsVisible);
    }

    [Fact]
    public async Task Escolher_uma_categoria_filtra_a_lista_e_os_chips_e_as_regras_continuam_contando_tudo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        vm.SelectCategoryCommand.Execute(vm.Categories.Single(c => c.Category == QaCategories.Spaces));

        Assert.Equal(["espaco-duplo", "espaco-duplo"], IssueRows(vm).Select(r => r.RuleId));
        Assert.Equal([5, 1, 4], vm.SeverityFilters.Select(f => f.Count));
        Assert.Equal(3, vm.Categories.Single(c => c.Category == QaCategories.Spaces).Count);
        Assert.Equal([false, true], vm.Categories.Where(c => c.Category is null or QaCategories.Spaces).Select(c => c.IsSelected));

        // Gravidade e categoria somam: o chip de erros não deixa nada dos Espaços.
        vm.SelectSeverityCommand.Execute(vm.SeverityFilters.Single(f => f.Filter == ConferenceSeverityFilter.Errors));
        Assert.True(vm.IsEmpty);
        Assert.Equal("Nenhum problema com este filtro.", vm.EmptyMessage);
        Assert.Equal("Mostrando 0 de 5 problemas abertos · agrupados por script", vm.ListFooter);

        vm.SelectCategoryCommand.Execute(vm.Categories[0]);
        Assert.Equal(["traducao-vazia"], IssueRows(vm).Select(r => r.RuleId));
    }

    [Fact]
    public async Task Categoria_sem_problemas_explica_que_e_o_filtro()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        vm.SelectCategoryCommand.Execute(vm.Categories.Single(c => c.Category == QaCategories.Glossary));

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.SelectedRow);
        Assert.Equal("Nenhum problema com este filtro.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Cada_problema_com_corretor_traz_a_correcao_proposta_e_o_resto_nao()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var spaced = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        Assert.True(spaced.HasProposal);
        Assert.Equal("olá mundo", spaced.ProposedText);
        Assert.Equal("Corrigível", spaced.Tag);
        Assert.Equal("“olá  mundo”", spaced.Detail);
        Assert.Equal("olá  mundo", spaced.Translation);
        Assert.Equal("原文0", spaced.Original);
        Assert.Equal("Espaço duplo", spaced.Title);
        Assert.Equal("yst00001:0000", spaced.LineLabel);

        var empty = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");
        Assert.False(empty.HasProposal);
        Assert.Equal(string.Empty, empty.Tag);
        Assert.Equal("Tradução vazia", empty.Title);
        Assert.Equal(empty.Message, empty.Detail);
        Assert.False(empty.HasTranslation);

        Assert.Equal("Corrigir 2 automaticamente", vm.FixAllLabel);
    }

    [Fact]
    public async Task A_mesma_regra_apontada_duas_vezes_na_linha_e_um_problema_so_com_a_contagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        var row = IssueRows(vm).Single(r => r.UnitId == "yst00002:000005");

        Assert.Equal(2, row.Count);
        Assert.Equal("a b c", row.ProposedText);
        Assert.Equal(5, vm.SeverityFilters[0].Count);
    }

    [Fact]
    public async Task Faixa_de_erro_aparece_com_erro_aberto_e_conta_os_erros()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.True(vm.HasBlockingErrors);
        Assert.Equal("1 erro bloqueia o build.", vm.BlockingMessage);

        var root = SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Approved, TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [Unit("yst00001", 0, null, TranslationStatus.Approved), Unit("yst00001", 1, null, TranslationStatus.Reviewed)]);
        var many = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await many.RefreshAsync();
        Assert.Equal("2 erros bloqueiam o build.", many.BlockingMessage);

        var clean = new ConferenceViewModel { Project = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("limpo"), "Limpa", TranslationStatus.Approved)) };
        await clean.RefreshAsync();
        Assert.False(clean.HasBlockingErrors);
        Assert.Equal(string.Empty, clean.BlockingMessage);
    }

    [Fact]
    public async Task O_cabecalho_diz_quando_conferiu_quantas_linhas_e_quantas_regras()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var now = new DateTimeOffset(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);
        var vm = new ConferenceViewModel(clock: new FixedClock(now)) { Project = ProjectSummaryReader.Read(root) };

        Assert.Equal("Ainda não conferido", vm.CheckedSummary);

        await vm.RefreshAsync();

        var rules = QaEngine.Default.Rules.Count;
        Assert.Equal($"Última conferência agora · 1 linhas · {rules} regras ativas", vm.CheckedSummary);
    }

    [Fact]
    public async Task Sem_projeto_o_cabecalho_e_a_faixa_ficam_vazios()
    {
        var vm = new ConferenceViewModel();

        await vm.RefreshAsync();

        Assert.Equal(string.Empty, vm.CheckedSummary);
        Assert.False(vm.HasBlockingErrors);
        Assert.Equal("Corrigir automaticamente", vm.FixAllLabel);
        Assert.Null(vm.SelectedRow);
        Assert.False(vm.HasSelection);
    }

    [Fact]
    public async Task O_primeiro_problema_vem_selecionado_e_marcar_ok_passa_para_o_vizinho()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);

        Assert.Equal("traducao-vazia", vm.SelectedRow?.RuleId);
        Assert.True(vm.HasSelection);

        var first = IssueRows(vm).First(r => r.RuleId == "espaco-duplo");
        vm.SelectedRow = first;
        await vm.MarkOkCommand.ExecuteAsync(first);

        // O aviso saiu da lista; no lugar dele fica o que veio depois.
        Assert.Equal(("espaco-duplo", "yst00002:000005"), (vm.SelectedRow?.RuleId, vm.SelectedRow?.UnitId));
    }

    [Fact]
    public async Task Aplicar_correcao_grava_so_a_linha_avisa_reconfere_e_passa_para_o_proximo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var row = IssueRows(vm).Single(r => r.UnitId == "yst00001:000000" && r.RuleId == "espaco-duplo");
        vm.SelectedRow = row;

        await vm.ApplyFixCommand.ExecuteAsync(row);

        Assert.Equal("olá mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("a  b  c", folder.LoadText("yst00002")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
        Assert.Equal(1, changed);
        Assert.DoesNotContain(IssueRows(vm), r => r.UnitId == "yst00001:000000");
        Assert.Equal("Corrigir 1 automaticamente", vm.FixAllLabel);
        Assert.NotNull(vm.SelectedRow);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Aplicar_correcao_em_problema_sem_proposta_ou_sem_alvo_nao_faz_nada()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await Loaded(temp);
        var changed = 0;
        vm.TextsChanged += (_, _) => changed++;
        var empty = IssueRows(vm).Single(r => r.RuleId == "traducao-vazia");

        Assert.False(vm.ApplyFixCommand.CanExecute(empty));
        Assert.False(vm.ApplyFixCommand.CanExecute(null));
        await vm.ApplyFixCommand.ExecuteAsync(empty);
        await vm.ApplyFixCommand.ExecuteAsync(null);
        await vm.MarkOkCommand.ExecuteAsync(null);
        vm.OpenLineCommand.Execute(null);
        vm.SelectSeverityCommand.Execute(null);
        vm.SelectCategoryCommand.Execute(null);

        Assert.Equal(0, changed);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal("olá  mundo", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal(4, IssueRows(vm).Count());
    }

    [Fact]
    public async Task Clicar_no_chip_ja_marcado_mantem_o_filtro_e_o_estado_do_chip()
    {
        using var temp = new TempFolder();
        var (vm, _) = await Loaded(temp);
        var all = vm.SeverityFilters[0];
        var before = vm.Rows;

        all.IsSelected = false; // o ToggleButton desmarca o chip sozinho ao clique
        vm.SelectSeverityCommand.Execute(all);

        Assert.True(all.IsSelected);
        Assert.Same(before, vm.Rows);
    }

    [Fact]
    public void Toda_regra_do_QA_tem_titulo_e_explicacao_e_categoria_conhecida()
    {
        foreach (var rule in QaEngine.Default.Rules)
        {
            Assert.True(ConferenceRuleTexts.Has(rule.Id), $"Falta o texto da regra {rule.Id}.");
            Assert.NotEqual(QaCategories.Other, QaCategories.Of(rule.Id));
        }

        Assert.Equal("regra-nova", ConferenceRuleTexts.Title("regra-nova"));
        Assert.Equal("Faz algo.", ConferenceRuleTexts.Explanation("regra-nova", "Faz algo."));
    }

    [Theory]
    [InlineData("yst00012:000042", "yst00012:0042")]
    [InlineData("yst00012:123456", "yst00012:123456")]
    [InlineData("sem-dois-pontos", "sem-dois-pontos")]
    [InlineData("yst:abc", "yst:abc")]
    public void A_linha_aparece_em_mono_com_quatro_digitos(string unitId, string expected)
    {
        Assert.Equal(expected, ConferenceIssueRowViewModel.FormatLine(unitId));
    }

    [Theory]
    [InlineData("Tem certeza?  Você vai se molhar.", "Tem certeza? Você vai se molhar.", "“certeza?  Você”")]
    [InlineData("Uma frase bem longa antes do problema, anda !", "Uma frase bem longa antes do problema, anda!", "“anda !”")]
    [InlineData("a  b e depois um trecho comprido sem problema nenhum", "a b e depois um trecho comprido sem problema nenhum", "“a  b”")]
    public void O_trecho_mostra_o_que_a_correcao_muda_com_a_palavra_de_cada_lado(string before, string after, string expected)
    {
        Assert.Equal(expected, ConferenceIssueRowViewModel.Excerpt(before, after));
    }

    private sealed class FixGateRule(ManualResetEventSlim entered, ManualResetEventSlim gate) : IQaRule, IQaFixer
    {
        public string Id => "regra-porta";

        public string Description => "Corrige só depois que o teste liberar.";

        public IEnumerable<QaIssue> Check(QaContext context) => [];

        public string Fix(string translation)
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
            return translation;
        }
    }

    private sealed class ThrowingRule : IQaRule
    {
        public string Id => "regra-quebrada";

        public string Description => "Sempre lança.";

        public IEnumerable<QaIssue> Check(QaContext context) => throw new InvalidOperationException("regra quebrada.");
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
