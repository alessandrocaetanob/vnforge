using VNForge.Core.Eval;
using VNForge.Core.Text;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class GoldSetTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts() => new Dictionary<string, IReadOnlyList<TextUnit>>
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
    public void Cada_linha_leva_a_fala_de_antes_e_a_de_depois()
    {
        var gold = GoldSet.From(Texts());

        Assert.Equal(string.Empty, gold[0].Before);
        Assert.Equal("\"Dois\"", gold[0].After);
        Assert.Equal("\"Dois\"", gold[1].Before);
        Assert.Equal("\"四\"", gold[1].After);
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
}
