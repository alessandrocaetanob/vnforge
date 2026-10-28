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
    public void Quase_todas_as_falas_chegam_pendentes_e_uma_vem_da_IA_com_um_espaco_duplo_para_a_Conferencia()
    {
        var all = SampleProject.Texts.Values.SelectMany(u => u).ToList();

        Assert.Equal(29, all.Count(u => u.Status == TranslationStatus.Untranslated));
        var machine = Assert.Single(all, u => u.Status == TranslationStatus.Machine);
        Assert.Contains("  ", machine.Translation);
    }

    [Fact]
    public void A_traducao_de_uma_fala_desconhecida_e_nula()
    {
        Assert.Null(SampleProject.TranslationOf("これは別の文です。"));
    }

    [Fact]
    public void Create_grava_um_projeto_que_abre_marcado_como_exemplo_e_sem_nenhum_arquivo_de_jogo()
    {
        using var temp = new TempFolder();

        var root = SampleProject.Create(temp.Path);

        var summary = ProjectSummaryReader.Read(root);
        Assert.True(summary.IsSample);
        Assert.Equal(SampleProject.Name, summary.Name);
        Assert.Equal(3, summary.Scripts);
        Assert.Equal(30, summary.Counts.Untranslated + summary.Counts.Machine);
        Assert.StartsWith(temp.Path, root);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories),
            f => f.EndsWith(".ypf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        var folder = ProjectFolder.Open(root);
        Assert.NotEmpty(folder.LoadGlossary());
        Assert.NotEmpty(folder.LoadCharacters());
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
    public void Create_nao_deixa_a_pasta_temporaria_para_tras()
    {
        using var temp = new TempFolder();

        SampleProject.Create(temp.Path);

        Assert.Equal([SampleProject.FolderName], Directory.EnumerateDirectories(temp.Path).Select(Path.GetFileName));
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

    [Fact]
    public void A_Conferencia_aponta_o_espaco_duplo_da_fala_da_IA()
    {
        using var temp = new TempFolder();
        var folder = ProjectFolder.Open(SampleProject.Create(temp.Path));

        var report = QaEngine.Default.Run(QaContext.FromProject(folder));

        var issue = Assert.Single(report.Issues, i => i.RuleId == "espaco-duplo");
        Assert.Equal("yst00001:0003", issue.UnitId);
    }

    [Fact]
    public void Um_arquivo_exemplo_txt_qualquer_nao_faz_do_projeto_um_exemplo()
    {
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.Path);
        File.WriteAllText(Path.Combine(root, SampleProject.MarkerFileName), "minhas anotações");

        Assert.False(SampleProject.IsSample(root));
    }
}
