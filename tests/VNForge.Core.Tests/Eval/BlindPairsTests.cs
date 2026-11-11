using VNForge.Core.Eval;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class BlindPairsTests
{
    private static EvalConfigReport Report(string id, Func<string, string> text, params string[] lines) =>
        new(Config(id), [.. lines.Select(l => new EvalLineResult(l, text(l), 50))], 0, 0, 0, 0, 1m, 0m, 1);

    private static readonly string[] Ids = [.. Enumerable.Range(1, 30).Select(i => $"l{i:00}")];

    [Fact]
    public void Gera_o_numero_pedido_de_pares_sem_repetir_linha_na_mesma_combinacao()
    {
        EvalConfigReport[] reports = [Report("A", l => $"a {l}", Ids), Report("B", l => $"b {l}", Ids), Report("C", l => $"c {l}", Ids)];

        var pairs = BlindPairs.Generate(reports, 12, seed: 5);

        Assert.Equal(12, pairs.Count);
        Assert.Equal(Enumerable.Range(1, 12), pairs.Select(p => p.Index));
        Assert.All(pairs, p => Assert.NotEqual(p.First, p.Second));
        Assert.Equal(pairs.Count, pairs.Select(p => p.LineId).Distinct().Count());
    }

    [Fact]
    public void Textos_iguais_ou_recusados_nao_formam_par()
    {
        var refused = new EvalConfigReport(Config("B"), [new EvalLineResult("l01", "", 0, Refused: true), new EvalLineResult("l02", "igual", 50), new EvalLineResult("l03", "b", 50)], 0, 0, 0, 0, 1m, 0m, 1);
        var a = Report("A", l => l == "l02" ? "igual" : $"a {l}", "l01", "l02", "l03");

        var pairs = BlindPairs.Generate([a, refused], 10, seed: 1);

        Assert.Equal(["l03"], pairs.Select(p => p.LineId));
    }

    [Fact]
    public void Uma_configuracao_so_ou_nenhum_pedido_nao_gera_par()
    {
        Assert.Empty(BlindPairs.Generate([Report("A", l => l, Ids)], 12, 1));
        Assert.Empty(BlindPairs.Generate([Report("A", l => l, Ids), Report("B", l => l + "!", Ids)], 0, 1));
    }
}
