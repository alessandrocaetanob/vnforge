using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R5")]
public class AiUsageLogTests
{
    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    [Fact]
    public void Registro_ilegivel_recomeca_a_conta_em_vez_de_falhar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.CreateDirectory(projeto.CacheDirectory);
        File.WriteAllText(AiUsageLog.PathFor(projeto), "{ não é json");

        Assert.Equal(AiUsageTotals.Empty, AiUsageLog.Load(projeto));

        AiUsageLog.TryRecord(projeto, new AiUsageTotals(1, 1, 0, 0, 0.1m, 1));
        Assert.Equal(1, AiUsageLog.Load(projeto).Runs);
        Assert.Equal("{ não é json", File.ReadAllText(AiUsageLog.PathFor(projeto) + ".bad"));
    }
}
