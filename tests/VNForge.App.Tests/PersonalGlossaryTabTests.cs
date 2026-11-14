using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A aba "Meu glossário" (#537): termos pessoais, o estado de cada um neste projeto, "Usar no projeto" e "Guardar".</summary>
[Trait("Risco", "R2")]
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
    public async Task Termo_que_difere_do_projeto_nao_e_trocado_pelo_Usar()
    {
        var (vm, _) = await AbrirAsync(Alimentar());

        vm.Personal.UseCommand.Execute(vm.Personal.Rows.Single(r => r.Source == "遅刻"));

        Assert.Equal("atraso", vm.Terms.Single(t => t.Source == "遅刻").Target);
    }
}
