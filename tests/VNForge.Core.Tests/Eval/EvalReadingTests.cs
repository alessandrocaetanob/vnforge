using VNForge.Core.Eval;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class EvalReadingTests
{
    [Fact]
    public void A_referencia_e_a_de_maior_chrf()
    {
        Assert.Equal("A", EvalReading.Reference(Board())!.Config.Id);
    }

    [Fact]
    public void A_sugerida_e_a_mais_barata_que_chega_a_95_por_cento_da_referencia()
    {
        // B está a 99% de A e custa 1,6; C está a 97% e custa 1,0; D está a 88%.
        Assert.Equal("C", EvalReading.Recommended(Board())!.Config.Id);
    }

    [Fact]
    public void Sem_alternativa_barata_e_proxima_a_sugerida_e_a_propria_referencia()
    {
        var round = Board() with { Reports = [.. Board().Reports.Where(r => r.Config.Id is "A" or "D" or "E")] };

        Assert.Equal("A", EvalReading.Recommended(round)!.Config.Id);
        Assert.Contains("A tem o maior chrF, 61,4", EvalReading.Sentence(round));
    }

    [Fact]
    public void A_frase_diz_o_chrf_o_preco_a_escolha_as_cegas_e_a_pior_diferenca()
    {
        var round = Board() with { Pairs = [new BlindPair(1, "l1", "A", "C"), new BlindPair(2, "l2", "C", "A")] };
        round = round.WithAnswer(1, BlindChoice.Second).WithAnswer(2, BlindChoice.First);

        var sentence = EvalReading.Sentence(round);

        Assert.Equal(
            "C chega a 97% do chrF de A por metade do preço, e você preferiu C em 2 de 2 pares contra A. A pior diferença aparece em E, sem a bíblia.",
            sentence);
    }

    [Fact]
    public void Sem_resposta_as_cegas_a_frase_diz_que_a_comparacao_ainda_nao_foi_feita()
    {
        Assert.Contains("A rodada às cegas ainda não comparou C com A.", EvalReading.Sentence(Board()));
    }

    [Fact]
    public void A_pior_diferenca_explica_a_falta_da_biblia_quando_e_isso_que_muda()
    {
        var round = Board() with { Reports = [.. Board().Reports.Where(r => r.Config.Id is "A" or "C" or "D")] };

        Assert.EndsWith("A pior diferença aparece em D, sem a bíblia.", EvalReading.Sentence(round));
    }

    [Fact]
    public void Rodada_sem_relatorio_nao_tem_frase_nem_sugestao()
    {
        var round = Board() with { Reports = [] };

        Assert.Equal(string.Empty, EvalReading.Sentence(round));
        Assert.Null(EvalReading.Recommended(round));
    }
}
