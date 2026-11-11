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
    public void Enquanto_o_texto_nao_foi_lido_a_contagem_e_desconhecida()
    {
        var row = new GlossaryTermRow("先輩", "veterano");

        Assert.Null(row.Uses);
        Assert.Equal("…", row.UsesLabel);
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
    public async Task Abas_alternam_entre_termos_e_personagens()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);
        Assert.True(vm.IsCharactersTab);
        Assert.False(vm.IsTermsTab);
        Assert.Equal("Adicionar personagem", vm.AddLabel);

        vm.SelectedTab = GlossaryTab.Terms;

        Assert.False(vm.IsCharactersTab);
        Assert.True(vm.IsTermsTab);
        Assert.Equal("Adicionar termo", vm.AddLabel);
        Assert.Equal("Buscar termo, tradução ou observação", vm.SearchHint);
    }

    [Fact]
    public async Task Adicionar_da_aba_atual_cria_a_linha_certa()
    {
        using var temp = new TempFolder();
        var (vm, _) = await OpenAsync(temp);

        vm.AddCommand.Execute(null);
        Assert.Equal(3, vm.Terms.Count);
        Assert.Equal(3, vm.Characters.Count);

        vm.SelectedTab = GlossaryTab.Terms;
        vm.AddCommand.Execute(null);
        Assert.Equal(4, vm.Terms.Count);
        Assert.Equal(3, vm.Characters.Count);
    }
}
