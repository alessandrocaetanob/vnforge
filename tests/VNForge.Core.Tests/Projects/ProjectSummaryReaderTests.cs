using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectSummaryReaderTests
{
    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Linha(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    [Fact]
    public void Le_nome_engine_modelo_e_progresso_de_um_projeto_criado()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("yst00001", [Linha("yst00001", 1, TranslationStatus.Approved), Linha("yst00001", 2, TranslationStatus.Untranslated)]);
        projeto.SaveText("yst00002", [Linha("yst00002", 1, TranslationStatus.Machine), Linha("yst00002", 2, TranslationStatus.Untranslated)]);

        var resumo = ProjectSummaryReader.Read(temp.Path);

        Assert.Equal("Minha VN", resumo.Name);
        Assert.Equal(Path.GetFullPath(temp.Path), resumo.Root);
        Assert.Equal("yuris", resumo.EngineId);
        Assert.Equal(500, resumo.EngineVersion);
        Assert.Equal("fold", resumo.EncodingStrategy);
        Assert.Equal(AiConfig.Default.Model, resumo.Model);
        Assert.Equal(2, resumo.Scripts);
        Assert.Equal(4, resumo.Counts.Total);
        Assert.Equal(50, resumo.Counts.Percent);
    }

    [Fact]
    public void Projeto_sem_texto_extraido_tem_zero_linhas_e_zero_scripts()
    {
        using var temp = new TempFolder();
        NovoProjeto(temp.Path);

        var resumo = ProjectSummaryReader.Read(temp.Path);

        Assert.Equal(0, resumo.Scripts);
        Assert.Equal(0, resumo.Counts.Total);
    }

    [Fact]
    public void Pasta_sem_vnforge_json_nao_e_projeto()
    {
        using var temp = new TempFolder();

        Assert.Throws<ProjectConfigException>(() => ProjectSummaryReader.Read(temp.Path));
    }

    [Fact]
    public void Texto_invalido_propaga_o_erro_do_arquivo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.CreateDirectory(projeto.TextDirectory);
        File.WriteAllText(Path.Combine(projeto.TextDirectory, "yst00001.json"), "{ isto não é json");

        Assert.Throws<TextFileException>(() => ProjectSummaryReader.Read(temp.Path));
    }
}

public class StatusDistributionTests
{
    [Fact]
    public void Percentuais_somam_100_e_seguem_a_ordem_aprovadas_revisadas_traduzidas_ia_pendentes()
    {
        var counts = new StatusCounts(Untranslated: 38, Machine: 17, Translated: 6, Reviewed: 18, Approved: 21);

        var d = StatusDistribution.Of(counts);

        Assert.Equal((21, 18, 6, 17, 38), (d.Approved, d.Reviewed, d.Translated, d.Machine, d.Pending));
        Assert.Equal(100, d.Approved + d.Reviewed + d.Translated + d.Machine + d.Pending);
    }

    [Fact]
    public void Arredondamento_usa_o_maior_resto_e_ainda_soma_100()
    {
        var counts = new StatusCounts(1, 1, 1, 0, 0);

        var d = StatusDistribution.Of(counts);

        Assert.Equal(100, d.Approved + d.Reviewed + d.Translated + d.Machine + d.Pending);
        Assert.Equal(34, d.Pending);
    }

    [Fact]
    public void Sem_linhas_tudo_fica_pendente_em_zero_por_cento_de_cada()
    {
        var d = StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0));

        Assert.Equal(0, d.Approved + d.Reviewed + d.Translated + d.Machine);
        Assert.Equal(100, d.Pending);
    }
}
