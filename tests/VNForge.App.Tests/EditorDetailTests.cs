using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class EditorDetailTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly string raiz;

    public EditorDetailTests() =>
        raiz = SampleProjects.Create(pasta.Path, "Teste", TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Untranslated);

    public void Dispose() => pasta.Dispose();

    private async Task<EditorViewModel> Abrir()
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Teste", raiz);
        await vm.Loading;
        return vm;
    }

    private TextUnit Gravada(int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public async Task Selecionar_uma_linha_cria_o_painel_com_a_unidade_dela()
    {
        var vm = await Abrir();

        Assert.NotNull(vm.Detail);
        Assert.Equal("yst00001:000000", vm.Detail.Id);
        Assert.Equal("yst00001", vm.Detail.ScriptKey);
        Assert.Equal("原文0", vm.Detail.Source);

        vm.SelectedLine = vm.Lines[1];

        Assert.Equal("yst00001:000001", vm.Detail?.Id);
        Assert.Equal("tradução 1", vm.Detail?.Translation);
    }

    [Fact]
    public async Task Linha_com_aviso_do_QA_mostra_o_mesmo_aviso_no_painel_e_ele_some_ao_gravar()
    {
        var vm = await Abrir();
        Assert.True(vm.Lines[0].HasWarning);
        var aviso = Assert.Single(vm.Detail!.QaIssues);
        Assert.Equal(ProjectLabels.Sentence(vm.Lines[0].Entry.Issues[0].Message), aviso.Message);
        Assert.False(aviso.IsError);
        Assert.Contains(ProjectLabels.Sentence(vm.Lines[0].Entry.Issues[0].Message), vm.Lines[0].IssueTip);

        vm.Detail.Translation = "Bom dia.";
        vm.Detail.Flush();

        Assert.False(vm.Lines[0].HasWarning);
        Assert.False(vm.Detail.HasQaIssues);
    }

    private string ProjetoComTilJapones(string subpasta, bool pontuacaoDoTeclado)
    {
        var projeto = ProjectFolder.Create(Path.Combine(pasta.Path, subpasta), new ProjectConfig(
            "Real",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default)
        {
            Style = StyleConfig.Default with { KeyboardPunctuation = pontuacaoDoTeclado },
        });
        projeto.SaveText("story04", [new TextUnit("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine)]);
        return projeto.Root;
    }

    private async Task<EditorViewModel> AbrirProjeto(string raizDoProjeto)
    {
        var vm = new EditorViewModel(clock: relogio, postToUi: acao => acao());
        vm.Project = EditorFixtures.Summary("Real", raizDoProjeto);
        await vm.Loading;
        return vm;
    }

    [Fact]
    public async Task Erro_de_caractere_fora_da_codificacao_some_do_painel_e_da_grade_ao_gravar_a_linha()
    {
        var vm = await AbrirProjeto(ProjetoComTilJapones("real-desligada", pontuacaoDoTeclado: false));
        Assert.All(vm.Detail!.QaIssues, i => Assert.True(i.IsError));
        Assert.NotEmpty(vm.Detail.QaIssues);
        Assert.True(vm.Lines[0].HasError);

        vm.Detail.Translation = "Manhã";
        vm.Detail.Flush();

        Assert.False(vm.Detail.HasQaIssues);
        Assert.False(vm.Lines[0].HasError);
        Assert.Equal(0, vm.IssueCount);
    }

    [Fact]
    public async Task Gravar_o_til_japones_com_a_opcao_ligada_grava_o_til_do_teclado_e_limpa_o_erro()
    {
        var vm = await AbrirProjeto(ProjetoComTilJapones("real-ligada", pontuacaoDoTeclado: true));
        Assert.True(vm.Detail!.HasQaIssues);

        vm.Detail.Translation = "～Manhã～。";
        vm.Detail.Flush();

        Assert.Equal("~Manhã~.", ProjectFolder.Open(vm.Project!.Root).LoadText("story04")[0].Translation);
        Assert.False(vm.Detail.HasQaIssues);
    }

    [Fact]
    public async Task Gravar_um_caractere_novo_fora_da_codificacao_acende_o_erro_no_painel()
    {
        var vm = await AbrirProjeto(ProjetoComTilJapones("real-novo", pontuacaoDoTeclado: false));
        vm.Detail!.Translation = "Manhã";
        vm.Detail.Flush();
        Assert.False(vm.Detail.HasQaIssues);

        vm.Detail.Translation = "Manhã。";
        vm.Detail.Flush();

        var erro = Assert.Single(vm.Detail.QaIssues);
        Assert.True(erro.IsError);
        Assert.Contains("U+3002", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mensagem_do_QA_no_inspetor_abre_com_maiuscula()
    {
        var issue = new VNForge.Core.Qa.QaIssue("traducao-inconsistente", VNForge.Core.Qa.QaSeverity.Warning, "yst00001:000001", "yst00001", "o original “x” aparece traduzido como “y” em outras linhas");

        var shown = Assert.Single(LineIssueViewModel.From([issue]));

        Assert.Equal("O original “x” aparece traduzido como “y” em outras linhas", shown.Message);
    }

    [Fact]
    public async Task Linha_sem_aviso_nao_mostra_bloco_de_avisos()
    {
        var vm = await Abrir();

        vm.SelectedLine = vm.Lines[1];

        Assert.False(vm.Lines[1].HasWarning);
        Assert.False(vm.Detail!.HasQaIssues);
    }

    [Fact]
    public async Task Sem_linha_selecionada_nao_ha_painel()
    {
        var vm = await Abrir();

        vm.SelectedLine = null;

        Assert.Null(vm.Detail);
    }

    [Fact]
    public async Task Editar_trocar_de_linha_e_voltar_mostra_o_texto_novo()
    {
        var vm = await Abrir();
        var primeira = vm.Lines[0];
        vm.Detail!.Translation = "Bom dia.";

        vm.SelectedLine = vm.Lines[1];
        Assert.Equal("Bom dia.", Gravada(0).Translation);

        vm.SelectedLine = primeira;

        Assert.Equal("Bom dia.", vm.Detail?.Translation);
        Assert.Equal(TranslationStatus.Translated, vm.Detail?.Status);
    }

    [Fact]
    public async Task Trocar_de_linha_descarta_o_painel_anterior_sem_deixar_gravacao_agendada()
    {
        var vm = await Abrir();
        var anterior = vm.Detail!;
        anterior.Translation = "Pendente";

        vm.SelectedLine = vm.Lines[1];
        anterior.Translation = "Depois de descartado";
        relogio.Advance(TimeSpan.FromSeconds(5));

        Assert.NotSame(anterior, vm.Detail);
        Assert.Equal("Pendente", Gravada(0).Translation);
    }

    [Fact]
    public async Task Ctrl_Enter_aprova_e_move_a_selecao_para_a_proxima_linha()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Bom dia.";

        vm.Detail.ApproveAndNextCommand.Execute(null);

        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.Equal("yst00001:000001", vm.Detail?.Id);
        var lida = Gravada(0);
        Assert.Equal(TranslationStatus.Approved, lida.Status);
        Assert.Equal("Bom dia.", lida.Translation);
        Assert.Equal(1, ProjectStatusReporter.Create(ProjectFolder.Open(raiz)).Total.Approved);
    }

    [Fact]
    public async Task Alt_Cima_e_Alt_Baixo_do_painel_movem_a_selecao_e_gravam_antes()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Primeira";

        vm.Detail.NextCommand.Execute(null);
        Assert.Equal("yst00001:000001", vm.SelectedLine?.Id);
        Assert.Equal("Primeira", Gravada(0).Translation);

        vm.Detail!.PreviousCommand.Execute(null);
        Assert.Equal("yst00001:000000", vm.SelectedLine?.Id);
    }

    [Fact]
    public async Task Aprovar_a_ultima_linha_mantem_a_selecao_e_o_painel()
    {
        var vm = await Abrir();
        vm.SelectedLine = vm.Lines[^1];
        vm.Detail!.Translation = "Fim.";

        vm.Detail.ApproveAndNextCommand.Execute(null);

        Assert.Equal("yst00001:000002", vm.SelectedLine?.Id);
        Assert.Equal(TranslationStatus.Approved, vm.Detail?.Status);
    }

    [Fact]
    public async Task Gravacao_que_falha_mantem_a_selecao_e_o_painel_com_a_mensagem()
    {
        var vm = await Abrir();
        var primeira = vm.SelectedLine;
        var painel = vm.Detail!;
        painel.Translation = "Não vai gravar";
        File.Delete(ProjectFolder.Open(raiz).TextPath("yst00001"));

        vm.SelectedLine = vm.Lines[1];

        Assert.Same(primeira, vm.SelectedLine);
        Assert.Same(painel, vm.Detail);
        Assert.NotNull(painel.ErrorMessage);
        Assert.Equal("Não vai gravar", painel.Translation);
    }

    [Fact]
    public async Task Navegar_pelo_painel_com_gravacao_que_falha_nao_sai_da_linha()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Não vai gravar";
        File.Delete(ProjectFolder.Open(raiz).TextPath("yst00001"));

        vm.SelectNextCommand.Execute(null);

        Assert.Equal("yst00001:000000", vm.SelectedLine?.Id);
        Assert.NotNull(vm.Detail?.ErrorMessage);
    }

    [Fact]
    public async Task Depois_que_o_arquivo_volta_a_selecao_pode_seguir()
    {
        var vm = await Abrir();
        var caminho = ProjectFolder.Open(raiz).TextPath("yst00001");
        var original = File.ReadAllText(caminho);
        vm.Detail!.Translation = "Agora vai";
        File.Delete(caminho);
        vm.SelectedLine = vm.Lines[1];
        Assert.Equal("yst00001:000000", vm.SelectedLine?.Id);

        File.WriteAllText(caminho, original);
        vm.SelectedLine = vm.Lines[1];

        Assert.Equal("yst00001:000001", vm.Detail?.Id);
        Assert.Equal("Agora vai", Gravada(0).Translation);
    }

    [Fact]
    public async Task Trocar_de_projeto_grava_e_descarta_o_painel()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Antes de trocar";

        vm.Project = EditorFixtures.Summary("Outro", Path.Combine(pasta.Path, "inexistente"));
        await vm.Loading;

        Assert.Null(vm.Detail);
        Assert.Null(vm.SelectedLine);
        Assert.Equal("Antes de trocar", Gravada(0).Translation);
    }

    [Fact]
    public async Task Filtro_que_tira_a_linha_da_grade_grava_e_troca_o_painel()
    {
        var vm = await Abrir();
        vm.Detail!.Translation = "Traduzida agora";
        var chip = vm.Filters.Single(f => f.Filter == LineStatusFilter.Machine);

        vm.SelectFilterCommand.Execute(chip);

        Assert.Equal("Traduzida agora", Gravada(0).Translation);
        Assert.Equal("yst00001:000001", vm.Detail?.Id);
    }
}
