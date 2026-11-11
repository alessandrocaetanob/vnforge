using VNForge.Core.Eval;
using VNForge.Core.Text;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class GoldSetTests
{
    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts() => new Dictionary<string, IReadOnlyList<TextUnit>>
    {
        ["s1"] =
        [
            Unit("s1:1", TranslationStatus.Approved, "一", "Um", "Kenta"),
            Unit("s1:2", TranslationStatus.Machine, "二", "Dois"),
            Unit("s1:3", TranslationStatus.Approved, "三", "Três", "Misaki"),
            Unit("s1:4", TranslationStatus.Untranslated, "四"),
            Unit("s1:5", TranslationStatus.Approved, "五", "   "),
        ],
        ["s2"] =
        [
            Unit("s2:1", TranslationStatus.Approved, "六", "Seis"),
            Unit("s2:2", TranslationStatus.Untranslated, "七"),
            Unit("s2:3", TranslationStatus.Untranslated, ""),
            Unit("s2:4", TranslationStatus.Keep, "八", "八"),
        ],
    };

    [Fact]
    public void So_as_linhas_aprovadas_com_tradução_entram()
    {
        var gold = GoldSet.From(Texts());

        Assert.Equal(["s1:1", "s1:3", "s2:1"], gold.Select(g => g.Id));
        Assert.Equal("Um", gold[0].Reference);
        Assert.Equal("s1", gold[0].ScriptKey);
    }

    [Fact]
    public void Conta_as_linhas_que_faltam_ignorando_as_sem_original()
    {
        Assert.Equal(2, GoldSet.RemainingLines(Texts()));
    }

    [Fact]
    public void O_sorteio_e_estavel_para_a_mesma_semente_e_respeita_a_ordem_original()
    {
        var gold = Enumerable.Range(1, 50).Select(i => new GoldLine($"l{i:00}", "s", null, "x", "y")).ToList();

        var first = GoldSet.Sample(gold, 10, 42);
        var again = GoldSet.Sample(gold, 10, 42);
        var other = GoldSet.Sample(gold, 10, 43);

        Assert.Equal(10, first.Count);
        Assert.Equal(first.Select(g => g.Id), again.Select(g => g.Id));
        Assert.NotEqual(first.Select(g => g.Id), other.Select(g => g.Id));
        Assert.Equal(first.Select(g => g.Id).Order(StringComparer.Ordinal), first.Select(g => g.Id));
        Assert.Equal(10, first.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Amostra_maior_que_o_conjunto_devolve_o_conjunto_inteiro()
    {
        var gold = Enumerable.Range(1, 5).Select(i => new GoldLine($"l{i}", "s", null, "x", "y")).ToList();

        Assert.Equal(5, GoldSet.Sample(gold, 200, 1).Count);
    }

    private static List<GoldLine> Lines(string script, int from, int count) =>
        [.. Enumerable.Range(from, count).Select(i => new GoldLine($"{script}:{i:000}", script, null, "x", "y"))];

    [Fact]
    public void O_sorteio_pesa_o_trecho_pelo_tamanho_e_as_linhas_isoladas_ocupam_pouco_da_amostra()
    {
        // 50 linhas isoladas (posições pares) e 50 trechos de 10 linhas seguidas, em dois scripts.
        var gold = new List<GoldLine>();
        for (var i = 0; i < 50; i++)
        {
            gold.Add(new GoldLine($"a:{i * 2:0000}", "a", null, "x", "y", Position: i * 2));
        }

        for (var b = 0; b < 50; b++)
        {
            gold.AddRange(Enumerable.Range(0, 10).Select(k => new GoldLine($"b:{b * 12 + k:0000}", "b", null, "x", "y", Position: b * 12 + k)));
        }

        var singleShare = Enumerable.Range(1, 60)
            .Select(seed => GoldSet.SampleBlocks(gold, 100, seed, GoldSet.MaxBlockLines).SelectMany(b => b))
            .Average(lines => lines.Count(l => l.ScriptKey == "a") / (double)lines.Count());

        // Com o peso, cada sorteio cai numa linha isolada em 50 de 550 vezes (uns 1% das linhas da amostra). Sem o peso, metade dos trechos são isolados e eles seriam uns 9%.
        Assert.InRange(singleShare, 0.0, 0.04);
    }

    [Fact]
    public void Uma_linha_nao_aprovada_no_meio_abre_outro_trecho()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>
        {
            ["s1"] =
            [
                Unit("s1:1", TranslationStatus.Approved, "一", "Um"),
                Unit("s1:2", TranslationStatus.Approved, "二", "Dois"),
                Unit("s1:3", TranslationStatus.Machine, "三", "Três"),
                Unit("s1:4", TranslationStatus.Approved, "四", "Quatro"),
            ],
        };

        var blocks = GoldSet.SampleBlocks(GoldSet.From(texts), 100, 1, 10);

        Assert.Equal([["s1:1", "s1:2"], ["s1:4"]], blocks.Select(b => b.Select(l => l.Id).ToArray()));
    }

    [Fact]
    public void O_sorteio_de_trechos_fecha_o_total_corta_o_ultimo_e_repete_com_a_mesma_semente()
    {
        var gold = Lines("a", 1, 200);

        var blocks = GoldSet.SampleBlocks(gold, 45, 7, 10);
        var again = GoldSet.SampleBlocks(gold, 45, 7, 10);

        Assert.Equal(45, blocks.Sum(b => b.Count));
        Assert.Equal(blocks.SelectMany(b => b).Select(g => g.Id), again.SelectMany(b => b).Select(g => g.Id));
        Assert.NotEqual(blocks.SelectMany(b => b).Select(g => g.Id), GoldSet.SampleBlocks(gold, 45, 8, 10).SelectMany(b => b).Select(g => g.Id));
        Assert.All(blocks, b => Assert.Equal(b.Select(l => l.Id).Order(StringComparer.Ordinal), b.Select(l => l.Id)));
        var starts = blocks.Select(b => b[0].Id).ToList();
        Assert.Equal(starts.Order(StringComparer.Ordinal), starts);
        Assert.All(blocks, b => Assert.True(b.Count <= 10));
    }
}
