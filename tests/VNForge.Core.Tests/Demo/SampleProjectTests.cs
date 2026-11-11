using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Demo;

public class SampleProjectTests
{
    [Fact]
    public void A_cena_de_exemplo_tem_30_falas_em_japones_e_o_portugues_de_cada_uma()
    {
        var all = SampleProject.Texts.Values.SelectMany(u => u).ToList();

        Assert.Equal(30, SampleProject.LineCount);
        Assert.Equal(30, all.Count);
        Assert.All(all, unit => Assert.NotNull(SampleProject.TranslationOf(unit.Source)));
        Assert.Equal(30, all.Select(u => u.Source).Distinct().Count());
    }

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

    [Fact]
    public void Create_com_marcador_e_sem_vnforge_json_nao_apaga_as_traducoes()
    {
        using var temp = new TempFolder();
        var root = SampleProject.Create(temp.Path);
        var folder = ProjectFolder.Open(root);
        var key = folder.LoadAllText().Keys.Order(StringComparer.Ordinal).First();
        folder.SaveText(key, folder.LoadText(key).Select((u, i) => i == 0 ? u with { Translation = "Mudei.", Status = TranslationStatus.Approved } : u).ToList());
        File.Delete(Path.Combine(root, ProjectFolder.ConfigFileName));

        var error = Assert.Throws<ProjectConfigException>(() => SampleProject.Create(temp.Path));

        Assert.Contains(ProjectFolder.ConfigFileName, error.Message);
        Assert.Contains("Mudei.", File.ReadAllText(folder.TextPath(key)));
    }

    [Fact]
    public void Create_com_uma_pasta_de_mesmo_nome_e_outro_conteudo_nao_a_apaga()
    {
        using var temp = new TempFolder();
        var root = Path.Combine(temp.Path, SampleProject.FolderName);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "notas.txt"), "minhas notas");

        Assert.Throws<ProjectConfigException>(() => SampleProject.Create(temp.Path));

        Assert.Equal("minhas notas", File.ReadAllText(Path.Combine(root, "notas.txt")));
    }

    [Fact]
    public void Um_projeto_comum_nao_e_exemplo()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.Path);

        Assert.False(SampleProject.IsSample(root));
        Assert.False(ProjectSummaryReader.Read(root).IsSample);
    }
}
