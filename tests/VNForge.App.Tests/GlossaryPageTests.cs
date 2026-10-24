using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Busca, abas e contagem de linhas da página Glossário (#248).</summary>
public class GlossaryPageTests
{
    private static TextUnit Line(int n, string source, string? speaker = null) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, speaker, source, null, TranslationStatus.Untranslated);

    private static async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> OpenAsync(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001",
        [
            Line(0, "「先輩、遅刻です」", "美咲"),
            Line(1, "先輩と先輩の傘", "健太"),
            Line(2, "傘を忘れた", "美咲"),
            Line(3, "narração"),
        ]);
        folder.SaveGlossary([new GlossaryTerm("先輩", "veterano", "tratamento"), new GlossaryTerm("傘", "guarda-chuva"), new GlossaryTerm("文化祭", "festival cultural")]);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "animada"), new CharacterEntry("健太", "Kenta", CharacterGender.Male)]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.Loading;
        return (vm, folder);
    }

    [Fact]
    public async Task Mostra_quantas_linhas_do_projeto_tem_cada_termo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.Equal([2, 2, 0], vm.Terms.Select(t => t.Uses));
        Assert.Equal(["2", "2", "—"], vm.Terms.Select(t => t.UsesLabel));
    }

    [Fact]
    public async Task Mostra_quantas_linhas_fala_cada_personagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.Equal([2, 1], vm.Characters.Select(c => c.Uses));
    }

    [Fact]
    public void Enquanto_o_texto_nao_foi_lido_a_contagem_e_desconhecida()
    {
        var row = new GlossaryTermRow("先輩", "veterano");

        Assert.Null(row.Uses);
        Assert.Equal("…", row.UsesLabel);
    }

    [Fact]
    public async Task Editar_o_original_refaz_a_contagem_da_linha_e_nao_conta_como_alteracao_a_contagem()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        Assert.False(vm.IsDirty);

        vm.Terms[2].Source = "遅刻";

        Assert.Equal(1, vm.Terms[2].Uses);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task Termo_novo_comeca_com_zero_linhas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddTermCommand.Execute(null);

        Assert.Equal(0, vm.Terms[^1].Uses);
        Assert.Equal("—", vm.Terms[^1].UsesLabel);
    }

    [Fact]
    public async Task A_contagem_chega_sem_sujar_a_pagina()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resumos_trazem_a_quantidade_e_o_total_de_linhas()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        Assert.Equal("3 termos · 4 linhas no projeto", vm.TermsSummary);
        Assert.Equal("2 personagens · 3 linhas faladas", vm.CharactersSummary);
        Assert.Equal(3, vm.TermCount);
        Assert.Equal(2, vm.CharacterCount);
    }

    [Fact]
    public async Task Texto_ilegivel_nao_derruba_a_pagina_e_deixa_a_contagem_em_zero()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        File.WriteAllText(folder.TextPath("yst00001"), "{ não é json");

        vm.Reload();
        await vm.Loading;

        Assert.Equal(3, vm.Terms.Count);
        Assert.All(vm.Terms, t => Assert.Equal(0, t.Uses));
    }

    [Fact]
    public async Task Busca_filtra_termos_por_original_traducao_e_observacao()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.Search = "veter";
        Assert.Equal(["先輩"], vm.VisibleTerms.Select(t => t.Source));

        vm.Search = "TRATAMENTO";
        Assert.Equal(["先輩"], vm.VisibleTerms.Select(t => t.Source));

        vm.Search = "傘";
        Assert.Equal(["傘"], vm.VisibleTerms.Select(t => t.Source));
    }

    [Fact]
    public async Task Busca_filtra_personagens_por_original_nome_e_tom()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.Search = "kenta";
        Assert.Equal(["健太"], vm.VisibleCharacters.Select(c => c.Source));

        vm.Search = "animada";
        Assert.Equal(["美咲"], vm.VisibleCharacters.Select(c => c.Source));
    }

    [Fact]
    public async Task Limpar_a_busca_mostra_tudo_de_novo()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        vm.Search = "veter";

        vm.Search = "  ";

        Assert.Equal(3, vm.VisibleTerms.Count);
        Assert.Equal(2, vm.VisibleCharacters.Count);
    }

    [Fact]
    public async Task Busca_nao_apaga_do_arquivo_o_que_ela_esconde()
    {
        using var temp = new TempFolder();
        var (vm, folder) = await OpenAsync(temp);
        vm.Search = "veter";
        vm.VisibleTerms[0].Target = "veterana";

        vm.SaveCommand.Execute(null);

        Assert.Equal(3, folder.LoadGlossary().Count);
        Assert.Equal("veterana", folder.LoadGlossary()[0].Target);
    }

    [Fact]
    public async Task Sem_resultado_a_mensagem_cita_a_busca()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.Search = "xyz";

        Assert.Empty(vm.VisibleTerms);
        Assert.True(vm.NoVisibleTerms);
        Assert.Equal("Nada corresponde a \"xyz\".", vm.TermsEmptyText);
        Assert.Equal("Nada corresponde a \"xyz\".", vm.CharactersEmptyText);
    }

    [Fact]
    public void Sem_termos_a_mensagem_convida_a_adicionar()
    {
        var vm = new GlossaryViewModel();

        Assert.Equal("Nenhum termo ainda. Adicione o primeiro.", vm.TermsEmptyText);
        Assert.Equal("Nenhum personagem ainda. O nome original é o falante extraído do jogo.", vm.CharactersEmptyText);
    }

    [Fact]
    public async Task Adicionar_com_a_busca_ativa_limpa_a_busca_para_a_linha_nova_aparecer()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        vm.Search = "veter";

        vm.AddTermCommand.Execute(null);

        Assert.Equal(string.Empty, vm.Search);
        Assert.Equal(4, vm.VisibleTerms.Count);
        Assert.True(vm.VisibleTerms[^1].IsBlank);
    }

    [Fact]
    public async Task Remover_tira_a_linha_da_lista_visivel()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.RemoveTermCommand.Execute(vm.VisibleTerms[0]);

        Assert.Equal(2, vm.VisibleTerms.Count);
        Assert.Equal(2, vm.TermCount);
    }

    [Fact]
    public async Task Abas_alternam_entre_termos_e_personagens()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        Assert.True(vm.IsTermsTab);
        Assert.False(vm.IsCharactersTab);
        Assert.Equal("Adicionar termo", vm.AddLabel);

        vm.SelectedTab = GlossaryTab.Characters;

        Assert.False(vm.IsTermsTab);
        Assert.True(vm.IsCharactersTab);
        Assert.Equal("Adicionar personagem", vm.AddLabel);
        Assert.Equal("Buscar personagem", vm.SearchHint);
    }

    [Fact]
    public async Task Adicionar_da_aba_atual_cria_a_linha_certa()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddCommand.Execute(null);
        Assert.Equal(4, vm.Terms.Count);
        Assert.Equal(2, vm.Characters.Count);

        vm.SelectedTab = GlossaryTab.Characters;
        vm.AddCommand.Execute(null);
        Assert.Equal(4, vm.Terms.Count);
        Assert.Equal(3, vm.Characters.Count);
    }

    [Fact]
    public async Task Trocar_de_projeto_zera_busca_e_contagens()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        vm.Search = "veter";

        var other = SampleProjects.Create(temp.SubFolder("outro"), "Outra", TranslationStatus.Untranslated);
        vm.Project = ProjectSummaryReader.Read(other);
        await vm.Loading;

        Assert.Equal(string.Empty, vm.Search);
        Assert.Empty(vm.Terms);
        Assert.Empty(vm.VisibleTerms);
    }

    [Fact]
    public async Task Sem_projeto_nao_ha_contagem_a_esperar()
    {
        var vm = new GlossaryViewModel();

        await vm.Loading;

        Assert.Empty(vm.VisibleTerms);
    }
}
