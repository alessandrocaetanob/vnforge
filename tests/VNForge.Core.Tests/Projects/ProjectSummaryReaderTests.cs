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
        Assert.Equal(AiConfig.Default.Effort, resumo.Effort);
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

public class ProjectSummaryLastBuildTests
{
    private static readonly DateTime BuiltAt = new(2026, 10, 22, 0, 41, 0, DateTimeKind.Utc);

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    private static TextUnit Linha(string key, int n, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    /// <summary>Grava um pacote em <c>build/</c> e o <c>build-state.json</c> que o <c>vnf build</c> deixa, com <paramref name="replaced"/> scripts trocados.</summary>
    private static void GravarBuild(ProjectFolder projeto, DateTime quando, int replaced, bool comEstado = true)
    {
        var pacote = Path.Combine(projeto.BuildDirectory, "ysbin.ypf");
        File.WriteAllBytes(pacote, [1, 2, 3]);
        File.SetLastWriteTimeUtc(pacote, quando);
        if (comEstado)
        {
            Directory.CreateDirectory(projeto.CacheDirectory);
            File.WriteAllText(
                Path.Combine(projeto.CacheDirectory, "build-state.json"),
                $$"""{"version":1,"scripts":{},"archives":[{"gamePath":"ysbin.ypf","replacedFiles":{{replaced}},"length":3}],"warnings":[]}""");
        }
    }

    [Fact]
    public void Sem_arquivo_em_build_nao_ha_ultimo_build()
    {
        using var temp = new TempFolder();
        NovoProjeto(temp.Path);

        Assert.Null(ProjectSummaryReader.Read(temp.Path).LastBuild);
    }

    [Fact]
    public void Ultimo_build_tem_a_data_do_arquivo_mais_novo_e_a_quantidade_de_scripts_trocados()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        GravarBuild(projeto, BuiltAt, replaced: 14);

        var build = ProjectSummaryReader.Read(temp.Path).LastBuild;

        Assert.NotNull(build);
        Assert.Equal(new DateTimeOffset(BuiltAt), build.BuiltAt);
        Assert.Equal(14, build.Scripts);
    }

    [Fact]
    public void Build_sem_estado_conhece_a_data_mas_nao_os_scripts()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        GravarBuild(projeto, BuiltAt, replaced: 14, comEstado: false);

        var build = ProjectSummaryReader.Read(temp.Path).LastBuild;

        Assert.Equal(new DateTimeOffset(BuiltAt), build?.BuiltAt);
        Assert.Null(build?.Scripts);
    }

    [Fact]
    public void Estado_que_nao_confere_com_o_build_nao_informa_scripts()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        GravarBuild(projeto, BuiltAt, replaced: 14);
        File.WriteAllBytes(Path.Combine(projeto.BuildDirectory, "ysbin.ypf"), [1, 2, 3, 4, 5]);

        Assert.Null(ProjectSummaryReader.Read(temp.Path).LastBuild?.Scripts);
    }

    [Fact]
    public void Estado_ilegivel_nao_derruba_a_leitura_do_resumo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        GravarBuild(projeto, BuiltAt, replaced: 14);
        File.WriteAllText(Path.Combine(projeto.CacheDirectory, "build-state.json"), "{ quebrado");

        var build = ProjectSummaryReader.Read(temp.Path).LastBuild;

        Assert.NotNull(build);
        Assert.Null(build.Scripts);
    }

    [Fact]
    public void Ultimo_script_editado_e_o_de_texto_gravado_mais_recente_entre_os_que_tem_traducao()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("yst00001", [Linha("yst00001", 1, TranslationStatus.Approved)]);
        projeto.SaveText("yst00002", [Linha("yst00002", 1, TranslationStatus.Machine)]);
        projeto.SaveText("yst00003", [Linha("yst00003", 1, TranslationStatus.Untranslated)]);
        File.SetLastWriteTimeUtc(projeto.TextPath("yst00001"), BuiltAt.AddHours(2));
        File.SetLastWriteTimeUtc(projeto.TextPath("yst00002"), BuiltAt.AddHours(1));
        File.SetLastWriteTimeUtc(projeto.TextPath("yst00003"), BuiltAt.AddHours(3));

        Assert.Equal("yst00001", ProjectSummaryReader.Read(temp.Path).LastEditedScript);
    }

    [Fact]
    public void Sem_nenhuma_traducao_o_ultimo_script_editado_e_o_primeiro()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveText("yst00002", [Linha("yst00002", 1, TranslationStatus.Untranslated)]);
        projeto.SaveText("yst00001", [Linha("yst00001", 1, TranslationStatus.Untranslated)]);

        Assert.Equal("yst00001", ProjectSummaryReader.Read(temp.Path).LastEditedScript);
    }

    [Fact]
    public void Projeto_sem_scripts_nao_tem_ultimo_script_editado()
    {
        using var temp = new TempFolder();
        NovoProjeto(temp.Path);

        Assert.Null(ProjectSummaryReader.Read(temp.Path).LastEditedScript);
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
