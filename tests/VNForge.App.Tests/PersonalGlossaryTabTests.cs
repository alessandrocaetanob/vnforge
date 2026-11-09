using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba "Meu glossário" (#537): termos pessoais, o estado de cada um neste projeto, "Usar no projeto" e "Guardar".</summary>
public sealed class PersonalGlossaryTabTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private PersonalGlossary Pessoal() => new(Path.Combine(temp.Path, "config"));

    private async Task<(GlossaryViewModel Vm, ProjectFolder Folder)> AbrirAsync(PersonalGlossary? pessoal)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Untranslated);
        var folder = ProjectFolder.Open(root);
        folder.SaveGlossary([new GlossaryTerm("先輩", "senpai"), new GlossaryTerm("遅刻", "atraso"), new GlossaryTerm("傘", "guarda-chuva")]);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PersonalStore = pessoal };
        await vm.Loading;
        return (vm, folder);
    }

    private PersonalGlossary Alimentar()
    {
        var pessoal = Pessoal();
        pessoal.Upsert(new PersonalTerm("先輩", "senpai", "manter em romaji", ["a", "b"]));
        pessoal.Upsert(new PersonalTerm("ドキドキ", "tum-tum", "onomatopeia", ["a", "b", "c"]));
        pessoal.Upsert(new PersonalTerm("遅刻", "chegar tarde", null, ["a"]));
        return pessoal;
    }

    [Fact]
    public async Task A_aba_mostra_cada_termo_com_o_estado_no_projeto_e_de_quantos_projetos_vem()
    {
        var (vm, _) = await AbrirAsync(Alimentar());

        var linhas = vm.Personal.Rows;

        Assert.Equal(["先輩", "ドキドキ", "遅刻"], linhas.Select(r => r.Source));
        Assert.Equal([PersonalTermState.InProject, PersonalTermState.NotInProject, PersonalTermState.Differs], linhas.Select(r => r.State));
        Assert.Equal(["2 projetos", "3 projetos", "1 projeto"], linhas.Select(r => r.ProjectsText));
        Assert.Equal(3, vm.Personal.Count);
        Assert.Equal("1 de 3 termos entraram", vm.Personal.BannerTitle);
        Assert.Equal("3 termos", vm.Personal.ProjectTermsText);
        Assert.Equal(["遅刻 · atraso", "傘 · guarda-chuva"], vm.Personal.Candidates.Select(c => c.Label));
    }

    [Fact]
    public async Task Usar_no_projeto_poe_o_termo_na_tabela_Termos_e_ele_grava_com_o_projeto()
    {
        var (vm, folder) = await AbrirAsync(Alimentar());
        var dokidoki = vm.Personal.Rows.Single(r => r.Source == "ドキドキ");

        vm.Personal.UseCommand.Execute(dokidoki);

        Assert.Contains(vm.Terms, t => t.Source == "ドキドキ" && t.Target == "tum-tum" && t.Notes == "onomatopeia");
        Assert.Equal(PersonalTermState.InProject, vm.Personal.Rows.Single(r => r.Source == "ドキドキ").State);
        vm.SaveCommand.Execute(null);
        Assert.Contains(folder.LoadGlossary(), t => t.Source == "ドキドキ");
    }

    [Fact]
    public async Task Termo_que_difere_do_projeto_nao_e_trocado_pelo_Usar()
    {
        var (vm, _) = await AbrirAsync(Alimentar());

        vm.Personal.UseCommand.Execute(vm.Personal.Rows.Single(r => r.Source == "遅刻"));

        Assert.Equal("atraso", vm.Terms.Single(t => t.Source == "遅刻").Target);
    }

    [Fact]
    public async Task Guardar_leva_o_termo_do_projeto_ao_glossario_pessoal_e_anota_o_projeto()
    {
        var pessoal = Alimentar();
        var (vm, folder) = await AbrirAsync(pessoal);

        vm.Personal.KeepCommand.Execute(vm.Personal.Candidates.Single(c => c.Term.Source == "傘"));

        var termo = pessoal.Load().Single(t => t.Source == "傘");
        Assert.Equal("guarda-chuva", termo.Target);
        Assert.Equal([TranslationMemory.ProjectKeyOf(folder)], termo.Projects);
        Assert.Equal(4, vm.Personal.Count);
        Assert.DoesNotContain(vm.Personal.Candidates, c => c.Term.Source == "傘");
    }

    [Fact]
    public async Task Sem_glossario_pessoal_a_aba_fica_vazia_e_arquivo_ilegivel_vira_mensagem()
    {
        var (semArquivo, _) = await AbrirAsync(Pessoal());
        Assert.True(semArquivo.Personal.IsEmpty);
        Assert.False(semArquivo.Personal.HasBanner);

        var ilegivel = Pessoal();
        Directory.CreateDirectory(Path.GetDirectoryName(ilegivel.Path)!);
        File.WriteAllText(ilegivel.Path, "lixo");
        using var outra = new TempFolder();
        var root = SampleProjects.Create(outra.SubFolder("vn"), "Outra", TranslationStatus.Untranslated);
        var vm = new GlossaryViewModel { Project = ProjectSummaryReader.Read(root), PersonalStore = ilegivel };
        await vm.Loading;

        Assert.True(vm.Personal.HasError);
        Assert.Contains("glossário pessoal", vm.Personal.Error);
    }

    [Fact]
    public async Task Abrir_a_aba_relê_o_glossario_pessoal_e_esconde_busca_e_adicionar()
    {
        var pessoal = Pessoal();
        var (vm, _) = await AbrirAsync(pessoal);
        pessoal.Upsert(new PersonalTerm("先輩", "senpai"));

        vm.SelectedTab = GlossaryTab.Personal;

        Assert.Equal(["先輩"], vm.Personal.Rows.Select(r => r.Source));
        Assert.True(vm.IsPersonalTab);
        Assert.False(vm.ShowSearch);
        vm.SelectedTab = GlossaryTab.Terms;
        Assert.True(vm.ShowSearch);
    }
}
