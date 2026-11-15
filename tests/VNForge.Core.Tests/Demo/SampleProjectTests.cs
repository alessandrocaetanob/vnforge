using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Demo;

[Trait("Risco", "R6")]
public class SampleProjectTests
{
    [Fact]
    public void Create_de_novo_devolve_a_mesma_pasta_sem_apagar_o_que_a_pessoa_traduziu()
    {
        using var temp = new TempFolder();
        var root = SampleProject.Create(temp.Path);
        var folder = ProjectFolder.Open(root);
        var key = folder.LoadAllText().Keys.Order(StringComparer.Ordinal).First();
        var units = folder.LoadText(key).Select((u, i) => i == 0 ? u with { Translation = "Mudei.", Status = TranslationStatus.Approved } : u).ToList();
        folder.SaveText(key, units);

        Assert.Equal(root, SampleProject.Create(temp.Path));

        Assert.Equal("Mudei.", ProjectFolder.Open(root).LoadText(key)[0].Translation);
    }

    [Fact]
    public void Create_se_recupera_de_uma_criacao_interrompida_que_deixou_so_a_pasta_temporaria()
    {
        using var temp = new TempFolder();
        var leftover = Path.Combine(temp.Path, ".cena-de-exemplo-interrompida");
        Directory.CreateDirectory(Path.Combine(leftover, "text"));
        File.WriteAllText(Path.Combine(leftover, ProjectFolder.ConfigFileName), "{}");
        Directory.CreateDirectory(Path.Combine(temp.Path, SampleProject.FolderName));

        var root = SampleProject.Create(temp.Path);

        Assert.True(SampleProject.IsSample(root));
        Assert.Equal(30, ProjectSummaryReader.Read(root).Counts.Untranslated + ProjectSummaryReader.Read(root).Counts.Machine);
    }

    [Fact]
    public void Create_com_vnforge_json_e_sem_marcador_explica_e_nao_mexe_na_pasta()
    {
        using var temp = new TempFolder();
        var root = Path.Combine(temp.Path, SampleProject.FolderName);
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, ProjectFolder.ConfigFileName);
        File.WriteAllText(config, "{ \"meu\": true }");

        var error = Assert.Throws<ProjectConfigException>(() => SampleProject.Create(temp.Path));

        Assert.Contains("renomeie ou mova", error.Message);
        Assert.DoesNotContain(root, error.Message);
        Assert.Equal("{ \"meu\": true }", File.ReadAllText(config));
        Assert.False(File.Exists(Path.Combine(root, SampleProject.MarkerFileName)));
    }
}
