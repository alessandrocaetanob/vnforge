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
    public void Sem_projeto_as_tabelas_ficam_vazias_e_nao_salva()
    {
        var vm = new GlossaryViewModel();

        Assert.Empty(vm.Terms);
        Assert.Empty(vm.Characters);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Carrega_glossario_e_personagens_do_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveGlossary([new GlossaryTerm("先輩", "veterano", "tratamento")]);
        folder.SaveCharacters([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "formal")]);

        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root) };

        var term = Assert.Single(vm.Terms);
        Assert.Equal(("先輩", "veterano", "tratamento"), (term.Source, term.Target, term.Notes));
        var character = Assert.Single(vm.Characters);
        Assert.Equal(("美咲", "Misaki", CharacterGender.Female, "formal"), (character.Source, character.Target, character.Gender, character.Tone));
        Assert.False(vm.IsDirty);
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
    public void Editar_e_salvar_altera_characters_json()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);

        vm.AddCharacterCommand.Execute(null);
        var row = vm.Characters[0];
        row.Source = "美咲";
        row.Target = "Misaki";
        row.Gender = CharacterGender.Female;
        row.Tone = "formal";
        vm.SaveCommand.Execute(null);

        Assert.Equal([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "formal")], folder.LoadCharacters());
        Assert.False(File.Exists(folder.GlossaryPath));
    }

    [Fact]
    public void Remover_linha_e_salvar_tira_a_entrada_do_arquivo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveGlossary([new GlossaryTerm("a", "A"), new GlossaryTerm("b", "B")]);
        vm.Reload();

        vm.RemoveTermCommand.Execute(vm.Terms[0]);
        vm.SaveCommand.Execute(null);

        Assert.Equal([new GlossaryTerm("b", "B")], folder.LoadGlossary());
    }

    [Fact]
    public void Remover_personagem_e_salvar_tira_a_entrada_do_arquivo()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveCharacters([new CharacterEntry("a", "A"), new CharacterEntry("b", "B")]);
        vm.Reload();

        vm.RemoveCharacterCommand.Execute(vm.Characters[1]);
        vm.SaveCommand.Execute(null);

        Assert.Equal([new CharacterEntry("a", "A")], folder.LoadCharacters());
    }

    [Fact]
    public void Cadastrar_personagem_poe_o_nome_exato_no_alto_da_aba_Personagens()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        folder.SaveCharacters([new CharacterEntry("a", "A")]);
        vm.Reload();
        vm.SelectedTab = GlossaryTab.Terms;
        vm.Search = "filtro que esconderia a linha";

        var row = vm.RegisterCharacter("Ｃ氏");

        Assert.NotNull(row);
        Assert.Equal(GlossaryTab.Characters, vm.SelectedTab);
        Assert.Equal(string.Empty, vm.Search);
        Assert.Same(row, vm.Characters[0]);
        Assert.Same(row, vm.VisibleCharacters[0]);
        Assert.Equal(("Ｃ氏", string.Empty), (row.Source, row.Target));
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Cadastrar_personagem_e_salvar_grava_o_nome_sem_alterar_caractere()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);

        var row = vm.RegisterCharacter("Ｃ氏");
        Assert.NotNull(row);
        row.Target = "Cê";
        vm.SaveCommand.Execute(null);

        Assert.Equal([new CharacterEntry("Ｃ氏", "Cê")], folder.LoadCharacters());
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
    public void Termo_repetido_grava_o_primeiro_e_marca_a_segunda_linha()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        vm.AddTermCommand.Execute(null);
        vm.AddTermCommand.Execute(null);
        foreach (var row in vm.Terms)
        {
            row.Source = "先輩";
            row.Target = "veterano";
        }

        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsDirty);
        Assert.Equal([false, true], vm.Terms.Select(t => t.HasProblem));
        Assert.Equal(["先輩"], folder.LoadGlossary().Select(t => t.Source));
    }

    [Fact]
    public void Original_vazio_com_traducao_preenchida_fica_de_fora_e_marcado()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        vm.AddTermCommand.Execute(null);
        vm.Terms[0].Target = "veterano";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.Terms[0].HasProblem);
        Assert.Equal("Falta o termo em japonês.", vm.Terms[0].Problem);
        Assert.False(File.Exists(folder.GlossaryPath));
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
    public void Trocar_de_projeto_recarrega_e_zera_o_estado()
    {
        using var temp = new TempFolder();
        var (vm, _) = Open(temp);
        vm.AddTermCommand.Execute(null);
        var other = SampleProjects.Create(temp.SubFolder("outra"), "Outra", TranslationStatus.Approved);

        vm.Project = ProjectSummaryReader.Read(other);

        Assert.Empty(vm.Terms);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Editar_depois_de_salvar_marca_sujo_de_novo()
    {
        using var temp = new TempFolder();
        var (vm, _) = Open(temp);
        vm.AddTermCommand.Execute(null);
        vm.Terms[0].Source = "a";
        vm.Terms[0].Target = "A";
        vm.SaveCommand.Execute(null);

        vm.Terms[0].Target = "B";

        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveCommand.CanExecute(null));
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

    [Fact]
    public void Pasta_sem_permissao_de_escrita_vira_mensagem_e_nao_excecao()
    {
        using var temp = new TempFolder();
        var (vm, folder) = Open(temp);
        Directory.CreateDirectory(folder.GlossaryPath + ".tmp");
        vm.AddTermCommand.Execute(null);
        vm.Terms[0].Source = "a";
        vm.Terms[0].Target = "A";
        Directory.CreateDirectory(folder.GlossaryPath);

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Conversor_de_genero_mostra_o_rotulo_em_portugues()
    {
        var converter = GenderLabelConverter.Instance;

        Assert.Equal("Feminino", converter.Convert(CharacterGender.Female, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert("x", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("x", typeof(CharacterGender), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
