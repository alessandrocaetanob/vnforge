using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class GlossaryViewModelTests
{
    private static (GlossaryViewModel Vm, ProjectFolder Folder) Open(TempFolder temp)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };
        return (vm, ProjectFolder.Open(root));
    }

    [Fact]
    public void Editar_e_salvar_altera_glossary_json()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);

        vm.AddTermCommand.Execute(null);
        var row = vm.Terms[0];
        row.Source = " 先輩 ";
        row.Target = "veterano";
        row.Notes = "";
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal([new GlossaryTerm("先輩", "veterano")], folder.LoadGlossary());
        Assert.False(File.Exists(folder.CharactersPath));
    }

    [Fact]
    public void Salvar_o_glossario_mantem_romaji_apelidos_e_descricao_que_vieram_do_vndb()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, Romaji: "Misaki Todo", Aliases: ["Todo-sensei"], Description: "A professora.")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };

        vm.Characters[0].Tone = "formal";
        vm.SaveCommand.Execute(null);

        var saved = Assert.Single(folder.LoadCharacters());
        Assert.Equal(("formal", "Misaki Todo", "A professora."), (saved.Tone, saved.Romaji, saved.Description));
        Assert.Equal(["Todo-sensei"], saved.Aliases);
    }

    [Fact]
    public void Cadastrar_personagem_que_ja_existe_traz_a_linha_dele_em_vez_de_duplicar()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveCharacters([new CharacterEntry("a", "A"), new CharacterEntry("犬井", "Inui")]);
        vm.Reload();
        vm.SelectedTab = GlossaryTab.Terms;

        var row = vm.RegisterCharacter("犬井");

        Assert.Same(vm.Characters[1], row);
        Assert.Equal(2, vm.Characters.Count);
        Assert.Equal(GlossaryTab.Characters, vm.SelectedTab);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Cadastrar_personagem_sem_projeto_ou_com_arquivo_invalido_nao_faz_nada()
    {
        Assert.Null(new GlossaryViewModel().RegisterCharacter("犬井"));

        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        File.WriteAllText(ProjectFolder.Open(root).CharactersPath, "{ quebrado");
        var broken = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };

        Assert.Null(broken.RegisterCharacter("犬井"));
        Assert.Empty(broken.Characters);
    }

    [Fact]
    public void Linha_totalmente_vazia_e_descartada_ao_salvar()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);

        vm.AddTermCommand.Execute(null);
        vm.AddCharacterCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.False(File.Exists(folder.GlossaryPath));
        Assert.False(File.Exists(folder.CharactersPath));
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Arquivo_invalido_vira_aviso_e_bloqueia_o_salvamento()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        File.WriteAllText(ProjectFolder.Open(root).GlossaryPath, "{ quebrado");

        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Terms);
        vm.AddTermCommand.Execute(null);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal("{ quebrado", File.ReadAllText(ProjectFolder.Open(root).GlossaryPath));
    }

    [Fact]
    public void FindTerms_devolve_os_termos_da_linha_com_o_estado_atual_da_tabela()
    {
        using var temp = new TempFolder();
        var (vm, _) = Open(temp);
        vm.AddTermCommand.Execute(null);
        vm.Terms[0].Source = "先輩";
        vm.Terms[0].Target = "veterano";

        var found = vm.FindTerms("「先輩、待って」");

        Assert.Equal([new GlossaryTerm("先輩", "veterano")], found);
        Assert.Empty(vm.FindTerms("おはよう"));
    }

    [Fact]
    public void Reload_descarta_a_edicao_nao_salva()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveGlossary([new GlossaryTerm("a", "A")]);
        vm.Reload();
        vm.Terms[0].Target = "outro";
        vm.RemoveCharacterCommand.Execute(null);

        vm.Reload();

        Assert.Equal("A", vm.Terms[0].Target);
        Assert.False(vm.IsDirty);
        Assert.True(vm.NoCharacters);
        Assert.False(vm.NoTerms);
    }

    [Fact]
    public void Remover_todas_as_linhas_de_um_arquivo_existente_grava_a_lista_vazia()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveGlossary([new GlossaryTerm("a", "A")]);
        vm.Reload();

        vm.RemoveTermCommand.Execute(vm.Terms[0]);
        vm.SaveCommand.Execute(null);

        Assert.Empty(folder.LoadGlossary());
    }
}
