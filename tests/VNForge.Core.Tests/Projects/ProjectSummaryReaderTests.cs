using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

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
    public void Estado_que_nao_confere_com_o_build_nao_informa_scripts()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        GravarBuild(projeto, BuiltAt, replaced: 14);
        File.WriteAllBytes(Path.Combine(projeto.BuildDirectory, "ysbin.ypf"), [1, 2, 3, 4, 5]);

        Assert.Null(ProjectSummaryReader.Read(temp.Path).LastBuild?.Scripts);
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
}

public class StatusDistributionTests
{

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
