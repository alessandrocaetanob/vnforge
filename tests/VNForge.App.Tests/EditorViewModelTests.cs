using System.Diagnostics;
using VNForge.App.ViewModels;
using VNForge.Core;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

public class EditorViewModelTests
{
    private static ProjectLines DoisScripts() => Lines(
        new QaReport([new QaIssue("regra", QaSeverity.Warning, "yst00001:000002", "yst00001", "problema")], [], ["regra"]),
        ("yst00002", [Unit("yst00002", 1, "Ken", TranslationStatus.Approved)]),
        ("yst00001",
        [
            Unit("yst00001", 1, "Ana", TranslationStatus.Untranslated),
            Unit("yst00001", 2, "Ana", TranslationStatus.Machine),
            Unit("yst00001", 3, null, TranslationStatus.Untranslated),
            Unit("yst00001", 4, "Ken", TranslationStatus.Approved),
            Unit("yst00001", 5, "Ana", TranslationStatus.Reviewed),
        ]));

    private static async Task<EditorViewModel> Carregado(ProjectLines? lines = null)
    {
        var vm = new EditorViewModel(_ => lines ?? DoisScripts());
        vm.Project = Summary();
        await vm.Loading;
        return vm;
    }

    private static FilterChipViewModel Chip(EditorViewModel vm, LineStatusFilter filter) => vm.Filters.Single(f => f.Filter == filter);

    private static void Filtrar(EditorViewModel vm, LineStatusFilter filter) => vm.SelectFilterCommand.Execute(Chip(vm, filter));

    [Fact]
    public async Task Carga_seleciona_o_primeiro_script_e_preenche_as_linhas()
    {
        var vm = await Carregado();

        Assert.Equal(["yst00001", "yst00002"], vm.Scripts.Select(s => s.Key));
        Assert.Equal("yst00001", vm.SelectedScript?.Key);
        Assert.Equal("Cena 1", vm.ScriptTitle);
        Assert.Equal(5, vm.Lines.Count);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.Equal("2", vm.ScriptsCount);
        Assert.Equal("5 linhas nesta cena", vm.LinesSummary);
        Assert.Equal("Minha VN", vm.ProjectName);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Filtros_tem_os_rotulos_do_design_e_Todas_comeca_selecionado()
    {
        var vm = await Carregado();

        Assert.Equal(["Todas", "Em japonês", "Para revisar", "Com aviso", "Não traduzir", "Soa estranho"], vm.Filters.Select(f => f.Label));
        Assert.Equal([true, false, false, false, false, false], vm.Filters.Select(f => f.IsSelected));
    }

    [Fact]
    public async Task Chips_contam_as_linhas_do_script_atual()
    {
        var vm = await Carregado();

        Assert.Equal([5, 2, 1, 1, 0, 0], vm.Filters.Select(f => f.Count));
    }

    [Theory]
    [InlineData(LineStatusFilter.All, new[] { 1, 2, 3, 4, 5 })]
    [InlineData(LineStatusFilter.Pending, new[] { 1, 3 })]
    [InlineData(LineStatusFilter.Machine, new[] { 2 })]
    [InlineData(LineStatusFilter.Warnings, new[] { 2 })]
    public async Task Cada_filtro_mostra_so_as_linhas_dele(LineStatusFilter filtro, int[] esperadas)
    {
        var vm = await Carregado();

        Filtrar(vm, filtro);

        Assert.Equal(esperadas, vm.Lines.Select(l => l.Number));
        Assert.Equal(vm.Filters.Select(f => f.Filter == filtro), vm.Filters.Select(f => f.IsSelected));
    }

    [Fact]
    public async Task Trocar_o_filtro_troca_a_lista_inteira_sem_mexer_na_anterior()
    {
        var vm = await Carregado();
        var antes = vm.Lines;
        var mudancas = new List<string?>();
        vm.PropertyChanged += (_, e) => mudancas.Add(e.PropertyName);

        Filtrar(vm, LineStatusFilter.Pending);

        Assert.NotSame(antes, vm.Lines);
        Assert.Equal(5, antes.Count);
        Assert.Equal(1, mudancas.Count(n => n == nameof(EditorViewModel.Lines)));
        Assert.Equal("2 de 5 linhas nesta cena", vm.LinesSummary);
    }

    [Fact]
    public async Task Voltar_para_Todas_mostra_o_script_inteiro_de_novo()
    {
        var vm = await Carregado();
        Filtrar(vm, LineStatusFilter.Pending);

        Filtrar(vm, LineStatusFilter.All);

        Assert.Equal(5, vm.Lines.Count);
        Assert.Equal("5 linhas nesta cena", vm.LinesSummary);
    }

    [Fact]
    public async Task Seletor_de_personagem_lista_todos_a_narracao_e_os_falantes_do_que_mais_fala_ao_que_menos()
    {
        var vm = await Carregado();

        Assert.Equal(["Todos os personagens", "Narração", "Ana", "Ken"], vm.Speakers.Select(s => s.Label));
        Assert.Equal([null, LineFilter.Narration, "Ana", "Ken"], vm.Speakers.Select(s => s.Value));
        Assert.Equal(vm.Speakers[0], vm.SelectedSpeaker);
    }

    [Fact]
    public async Task Sem_linhas_de_narracao_a_opcao_nao_aparece()
    {
        var vm = await Carregado(Lines(("a", [Unit("a", 1, "Ana", TranslationStatus.Approved)])));

        Assert.Equal(["Todos os personagens", "Ana"], vm.Speakers.Select(s => s.Label));
    }

    [Fact]
    public async Task Escolher_personagem_filtra_e_recalcula_os_chips()
    {
        var vm = await Carregado();

        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "Ana");

        Assert.Equal([1, 2, 5], vm.Lines.Select(l => l.Number));
        Assert.Equal([3, 1, 1, 1, 0, 0], vm.Filters.Select(f => f.Count));
    }

    [Fact]
    public async Task Escolher_a_narracao_mostra_so_as_linhas_sem_falante()
    {
        var vm = await Carregado();

        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == LineFilter.Narration);

        Assert.Equal([3], vm.Lines.Select(l => l.Number));
        Assert.True(vm.Lines[0].IsNarration);
        Assert.Equal("Narração", vm.Lines[0].SpeakerLabel);
    }

    [Fact]
    public async Task Status_e_personagem_valem_juntos()
    {
        var vm = await Carregado();
        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "Ana");

        Filtrar(vm, LineStatusFilter.Pending);

        Assert.Equal([1], vm.Lines.Select(l => l.Number));
        Assert.Equal("1 de 5 linhas nesta cena", vm.LinesSummary);
    }

    [Fact]
    public async Task Selecao_continua_quando_a_linha_ainda_passa_no_filtro()
    {
        var vm = await Carregado();
        vm.SelectedLine = vm.Lines[2];

        Filtrar(vm, LineStatusFilter.Pending);

        Assert.Equal(3, vm.SelectedLine?.Number);
    }

    [Fact]
    public async Task Selecao_vai_para_a_primeira_linha_quando_a_selecionada_sai_do_filtro()
    {
        var vm = await Carregado();
        vm.SelectedLine = vm.Lines[3];

        Filtrar(vm, LineStatusFilter.Pending);

        Assert.Equal(1, vm.SelectedLine?.Number);
    }

    [Fact]
    public async Task Filtro_sem_resultado_deixa_a_selecao_vazia_e_explica()
    {
        var vm = await Carregado(Lines(("a", [Unit("a", 1, "Ana", TranslationStatus.Approved)])));

        Filtrar(vm, LineStatusFilter.Machine);

        Assert.Empty(vm.Lines);
        Assert.Null(vm.SelectedLine);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Nenhuma linha com este filtro.", vm.EmptyMessage);
    }

    [Fact]
    public async Task Proxima_e_anterior_andam_pela_lista_e_param_nas_pontas()
    {
        var vm = await Carregado();

        vm.SelectPreviousCommand.Execute(null);
        Assert.Equal(1, vm.SelectedLine?.Number);

        vm.SelectNextCommand.Execute(null);
        vm.SelectNextCommand.Execute(null);
        Assert.Equal(3, vm.SelectedLine?.Number);

        for (var i = 0; i < 10; i++)
        {
            vm.SelectNextCommand.Execute(null);
        }

        Assert.Equal(5, vm.SelectedLine?.Number);
        vm.SelectPreviousCommand.Execute(null);
        Assert.Equal(4, vm.SelectedLine?.Number);
    }

    [Fact]
    public async Task Proxima_e_anterior_sem_selecao_escolhem_a_primeira()
    {
        var vm = await Carregado();
        vm.SelectedLine = null;
        vm.SelectNextCommand.Execute(null);
        Assert.Equal(1, vm.SelectedLine?.Number);

        vm.SelectedLine = null;
        vm.SelectPreviousCommand.Execute(null);
        Assert.Equal(1, vm.SelectedLine?.Number);
    }

    [Fact]
    public async Task Proxima_anda_so_entre_as_linhas_filtradas()
    {
        var vm = await Carregado();
        Filtrar(vm, LineStatusFilter.Pending);

        vm.SelectNextCommand.Execute(null);

        Assert.Equal(3, vm.SelectedLine?.Number);
    }

    [Fact]
    public async Task Proxima_sem_linhas_nao_faz_nada()
    {
        var vm = await Carregado(Lines(("vazio", [])));

        vm.SelectNextCommand.Execute(null);
        vm.SelectPreviousCommand.Execute(null);

        Assert.Null(vm.SelectedLine);
    }

    [Fact]
    public async Task Trocar_de_script_reinicia_o_personagem_e_mantem_o_filtro_de_status()
    {
        var vm = await Carregado();
        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "Ana");
        Filtrar(vm, LineStatusFilter.Pending);

        vm.SelectedScript = vm.Scripts[1];

        Assert.Equal("Cena 2", vm.ScriptTitle);
        Assert.Equal(["Todos os personagens", "Ken"], vm.Speakers.Select(s => s.Label));
        Assert.Equal(vm.Speakers[0], vm.SelectedSpeaker);
        Assert.Empty(vm.Lines);
        Assert.True(Chip(vm, LineStatusFilter.Pending).IsSelected);
        Assert.Equal([1, 0, 0, 0, 0, 0], vm.Filters.Select(f => f.Count));
        Assert.Null(vm.SelectedLine);

        Filtrar(vm, LineStatusFilter.All);
        Assert.Equal("yst00002:000001", vm.SelectedLine?.Id);
    }

    [Fact]
    public async Task Cabecalho_acompanha_a_distribuicao_do_script_selecionado()
    {
        var vm = await Carregado();

        Assert.Equal(StatusDistribution.Of(new StatusCounts(2, 1, 0, 1, 1)), vm.Distribution);
        Assert.StartsWith("Aprovadas 20%", vm.DistributionDescription);
    }

    [Fact]
    public async Task Item_do_script_mostra_percentual_concluido_e_a_IA_no_tooltip_junto_com_o_id()
    {
        var vm = await Carregado();
        var item = vm.Scripts[0];

        Assert.Equal("40%", item.PercentLabel);
        Assert.Equal($"{item.Name}\n{item.Key} · IA 20%", item.Tooltip);
        Assert.False(item.IsComplete);
        Assert.Equal("5", item.LinesLabel);
        Assert.True(vm.Scripts[1].IsComplete);
    }

    [Fact]
    public async Task Linha_expoe_os_textos_da_grade()
    {
        var vm = await Carregado();

        var pendente = vm.Lines[0];
        Assert.Equal("Em japonês", pendente.StatusLabel);
        Assert.Equal("sem tradução", pendente.TranslationLabel);
        Assert.False(pendente.HasTranslation);
        Assert.Equal("原文1", pendente.Source);
        Assert.Equal("Ana", pendente.SpeakerLabel);
        Assert.False(pendente.IsNarration);
        Assert.Equal(string.Empty, pendente.IssueLabel);

        var ia = vm.Lines[1];
        Assert.Equal("tradução 2", ia.TranslationLabel);
        Assert.True(ia.HasWarning);
        Assert.False(ia.HasError);
        Assert.Equal("1 aviso do QA", ia.IssueLabel);
    }

    [Fact]
    public void Rotulo_do_QA_conta_erros_e_avisos()
    {
        var unit = Unit("a", 1, null, TranslationStatus.Approved);

        string Rotulo(params QaSeverity[] severities) =>
            new LineViewModel(new LineEntry("a", 1, unit, [.. severities.Select(s => new QaIssue("r", s, unit.Id, "a", "m"))])).IssueLabel;

        Assert.Equal("2 avisos do QA", Rotulo(QaSeverity.Warning, QaSeverity.Warning));
        Assert.Equal("1 erro do QA", Rotulo(QaSeverity.Error));
        Assert.Equal("2 erros e 1 aviso do QA", Rotulo(QaSeverity.Error, QaSeverity.Warning, QaSeverity.Error));
        Assert.True(new LineViewModel(new LineEntry("a", 1, unit, [new QaIssue("r", QaSeverity.Error, unit.Id, "a", "m")])).HasError);
    }

    [Fact]
    public async Task Detail_fica_nulo_quando_a_pasta_do_projeto_nao_abre()
    {
        var vm = await Carregado();

        Assert.NotNull(vm.SelectedLine);
        Assert.Null(vm.Detail);
    }

    [Fact]
    public async Task Erro_do_Core_vira_mensagem_em_portugues_e_a_grade_fica_vazia()
    {
        var vm = new EditorViewModel(_ => throw new VNForgeException("O arquivo text/a.json está inválido."));
        vm.Project = Summary();
        await vm.Loading;

        Assert.Equal("Não foi possível ler as linhas do projeto: O arquivo text/a.json está inválido. A grade fica vazia.", vm.ErrorMessage);
        Assert.Empty(vm.Scripts);
        Assert.Empty(vm.Lines);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.IsLoading);
        Assert.Null(vm.SelectedScript);
    }

    [Fact]
    public async Task Erro_de_IO_tambem_vira_mensagem()
    {
        var vm = new EditorViewModel(_ => throw new IOException("disco cheio"));
        vm.Project = Summary();
        await vm.Loading;

        Assert.Contains("disco cheio", vm.ErrorMessage);
    }

    [Fact]
    public async Task Carga_bem_sucedida_limpa_o_erro_anterior()
    {
        var falha = true;
        var vm = new EditorViewModel(_ => falha ? throw new VNForgeException("quebrou") : DoisScripts());
        vm.Project = Summary();
        await vm.Loading;
        Assert.NotNull(vm.ErrorMessage);

        falha = false;
        await vm.LoadAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.Scripts.Count);
    }

    [Fact]
    public async Task Projeto_sem_scripts_explica_como_extrair()
    {
        var vm = await Carregado(Lines());

        Assert.True(vm.IsEmpty);
        Assert.Equal("Este projeto ainda não tem texto extraído. Rode vnf extract.", vm.EmptyMessage);
        Assert.Equal("0", vm.ScriptsCount);
    }

    [Fact]
    public async Task Carga_mais_antiga_que_termina_depois_e_descartada()
    {
        using var liberar = new ManualResetEventSlim();
        var vm = new EditorViewModel(root =>
        {
            if (root == "primeiro")
            {
                liberar.Wait();
                return Lines(("velho", [Unit("velho", 1, null, TranslationStatus.Approved)]));
            }

            return Lines(("novo", [Unit("novo", 1, null, TranslationStatus.Approved)]));
        });

        vm.Project = Summary(root: "primeiro");
        var primeira = vm.Loading;
        vm.Project = Summary(name: "Outra", root: "segundo");
        await vm.Loading;
        liberar.Set();
        await primeira;

        Assert.Equal(["novo"], vm.Scripts.Select(s => s.Key));
        Assert.Equal("Cena 1", vm.ScriptTitle);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Enquanto_carrega_a_grade_avisa_que_esta_lendo()
    {
        using var liberar = new ManualResetEventSlim();
        var vm = new EditorViewModel(_ =>
        {
            liberar.Wait();
            return DoisScripts();
        });

        vm.Project = Summary();

        Assert.True(vm.IsLoading);
        Assert.Equal("Lendo as linhas do projeto...", vm.EmptyMessage);
        liberar.Set();
        await vm.Loading;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_a_grade_enquanto_o_novo_carrega()
    {
        using var liberar = new ManualResetEventSlim();
        var vm = new EditorViewModel(root =>
        {
            if (root == "B")
            {
                liberar.Wait();
                return Lines(("novo", [Unit("novo", 1, null, TranslationStatus.Approved)]));
            }

            return DoisScripts();
        });
        vm.Project = Summary("A", "A");
        await vm.Loading;
        Assert.Equal(5, vm.Lines.Count);

        vm.Project = Summary("B", "B");

        Assert.True(vm.IsLoading);
        Assert.Empty(vm.Scripts);
        Assert.Empty(vm.Lines);
        Assert.Null(vm.SelectedScript);
        Assert.Null(vm.SelectedLine);
        liberar.Set();
        await vm.Loading;

        Assert.False(vm.IsLoading);
        Assert.Equal(["novo"], vm.Scripts.Select(s => s.Key));
        Assert.Single(vm.Lines);
    }

    [Fact]
    public async Task Sem_projeto_a_grade_esvazia()
    {
        var vm = await Carregado();

        vm.Project = null;
        await vm.Loading;

        Assert.Empty(vm.Scripts);
        Assert.Empty(vm.Lines);
        Assert.Null(vm.SelectedScript);
    }

    [Fact]
    public async Task Projeto_real_e_lido_pelo_carregador_padrao_com_o_QA()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Untranslated, TranslationStatus.Machine);
        var vm = new EditorViewModel();

        vm.Project = ProjectSummaryReader.Read(root);
        await vm.Loading;

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(["yst00001"], vm.Scripts.Select(s => s.Key));
        Assert.Equal(3, vm.Lines.Count);
        Assert.Equal(1, Chip(vm, LineStatusFilter.Pending).Count);
        Assert.Equal(1, Chip(vm, LineStatusFilter.Machine).Count);
        Assert.Equal(1, Chip(vm, LineStatusFilter.Warnings).Count);
    }

    [Fact]
    public async Task Projeto_inexistente_no_carregador_padrao_vira_mensagem()
    {
        using var temp = new TempFolder();
        var vm = new EditorViewModel();

        vm.Project = Summary(root: Path.Combine(temp.Path, "nao-existe"));
        await vm.Loading;

        Assert.StartsWith("Não foi possível ler as linhas do projeto:", vm.ErrorMessage);
    }

    [Fact]
    public async Task Cinquenta_mil_linhas_trocam_de_filtro_sem_travar()
    {
        var vm = await Carregado(Large(50_000));
        Assert.Equal(50_000, vm.Lines.Count);

        var relogio = Stopwatch.StartNew();
        Filtrar(vm, LineStatusFilter.Pending);
        var pendentes = vm.Lines.Count;
        Filtrar(vm, LineStatusFilter.Warnings);
        var avisos = vm.Lines.Count;
        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Value == "Ana");
        var avisosDaAna = vm.Lines.Count;
        Filtrar(vm, LineStatusFilter.All);
        relogio.Stop();

        Assert.Equal(10_000, pendentes);
        Assert.Equal(7_143, avisos);
        Assert.InRange(avisosDaAna, 1, avisos - 1);
        Assert.Equal(12_500, vm.Lines.Count);
        Assert.InRange(relogio.ElapsedMilliseconds, 0, 1_000);
        Assert.Equal("12.500 de 50.000 linhas nesta cena", vm.LinesSummary);
    }
}
