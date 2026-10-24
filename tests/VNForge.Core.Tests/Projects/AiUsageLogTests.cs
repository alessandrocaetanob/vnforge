using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

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
    public void Sem_registro_o_total_e_zero()
    {
        using var temp = new TempFolder();

        Assert.Equal(AiUsageTotals.Empty, AiUsageLog.Load(NovoProjeto(temp.Path)));
    }

    [Fact]
    public void Cada_execucao_soma_ao_total_e_conta_como_uma()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        AiUsageLog.TryRecord(projeto, new AiUsageTotals(100, 50, 10, 5, 0.25m, 99));
        AiUsageLog.TryRecord(projeto, new AiUsageTotals(1, 2, 3, 4, 0.5m, 99));

        Assert.Equal(new AiUsageTotals(101, 52, 13, 9, 0.75m, 2), AiUsageLog.Load(projeto));
    }

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

    [Fact]
    public void Arquivo_que_nao_abre_agora_nao_perde_o_historico()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        AiUsageLog.TryRecord(projeto, new AiUsageTotals(100, 50, 0, 0, 1m, 1));
        var antes = File.ReadAllText(AiUsageLog.PathFor(projeto));

        using (new FileStream(AiUsageLog.PathFor(projeto), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AiUsageLog.TryRecord(projeto, new AiUsageTotals(1, 1, 0, 0, 0.1m, 1));
        }

        Assert.Equal(antes, File.ReadAllText(AiUsageLog.PathFor(projeto)));
        Assert.Equal(1, AiUsageLog.Load(projeto).Runs);
        Assert.Empty(Directory.GetFiles(projeto.CacheDirectory, "*.tmp"));
    }

    [Fact]
    public void Execucao_sem_preco_conta_como_sem_preco_e_nao_soma_dolar()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        AiUsageLog.TryRecord(projeto, new AiUsageTotals(10, 5, 0, 0, 1m, 1));
        AiUsageLog.TryRecord(projeto, new AiUsageTotals(10, 5, 0, 0, 0m, 1, UnpricedRuns: 1));

        var total = AiUsageLog.Load(projeto);
        Assert.Equal(2, total.Runs);
        Assert.Equal(1, total.UnpricedRuns);
        Assert.Equal(1m, total.Dollars);
    }

    [Fact]
    public void Valores_negativos_no_registro_sao_ignorados()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        Directory.CreateDirectory(projeto.CacheDirectory);
        File.WriteAllText(AiUsageLog.PathFor(projeto), """{"inputTokens":-5,"outputTokens":0,"cacheReadTokens":0,"cacheWriteTokens":0,"dollars":1,"runs":1}""");

        Assert.Equal(AiUsageTotals.Empty, AiUsageLog.Load(projeto));
    }
}
