using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R2")]
[Trait("Risco", "R7")]
public class GlossaryViewModelTests
{

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

}
