using VNForge.Core.Eval;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class EvalRoundTests
{

    [Fact]
    public void Projeta_custo_e_tempo_do_jogo_todo_pelas_linhas_medidas()
    {
        var report = Report(Config("C"), 59.8, dollars: 0.5m, seconds: 20, ids: Enumerable.Range(1, 10).Select(i => $"l{i}").ToArray());

        Assert.Equal(0.05m, report.ProjectedDollars(1));
        Assert.Equal(100m, report.ProjectedDollars(2000));
        Assert.Equal(50m, report.ProjectedBatchDollars(2000));
        Assert.Equal(TimeSpan.FromSeconds(4000), report.ProjectedTime(2000));
    }

    [Fact]
    public void Medias_de_chrf_e_do_juiz_ignoram_o_juiz_ausente()
    {
        var report = new EvalConfigReport(
            Config("A"),
            [new EvalLineResult("l1", "a", 80, 9), new EvalLineResult("l2", "b", 60, null), new EvalLineResult("l3", "", 0, null, Refused: true)],
            0, 0, 0, 0, 0m, 0m, 0);

        Assert.Equal(140.0 / 3, report.MeanChrf, 6);
        Assert.Equal(9.0, report.MeanJudge);
        Assert.Equal(1, report.RefusedLines);
        Assert.Null(new EvalConfigReport(Config("B"), [new EvalLineResult("l1", "a", 1)], 0, 0, 0, 0, 0m, 0m, 0).MeanJudge);
    }

    [Fact]
    public void Placar_as_cegas_conta_vitorias_so_nos_pares_que_a_configuracao_disputou()
    {
        var round = Board() with { Pairs = [new BlindPair(1, "l1", "A", "C"), new BlindPair(2, "l2", "C", "A"), new BlindPair(3, "l3", "B", "C"), new BlindPair(4, "l4", "A", "B")] };
        round = round
            .WithAnswer(1, BlindChoice.First)
            .WithAnswer(2, BlindChoice.First)
            .WithAnswer(3, BlindChoice.Tie)
            .WithAnswer(4, BlindChoice.Skipped);

        Assert.Equal((1, 3), round.BlindScore("C"));
        Assert.Equal((1, 2), round.BlindScore("A"));
        Assert.Equal((0, 1), round.BlindScore("B"));
        Assert.Equal((0, 0), round.BlindScore("E"));
        Assert.Equal((1, 2), round.HeadToHead("C", "A"));
        Assert.Equal(3, round.PairsDone);
    }

    [Fact]
    public void Responder_de_novo_troca_a_resposta_e_o_proximo_par_e_o_primeiro_sem_resposta()
    {
        var round = Board() with { Pairs = [new BlindPair(1, "l1", "A", "C"), new BlindPair(2, "l2", "A", "B")] };

        Assert.Equal(1, round.NextPair!.Index);
        round = round.WithAnswer(1, BlindChoice.First).WithAnswer(1, BlindChoice.Second);
        var answer = Assert.Single(round.Answers);
        Assert.Equal(BlindChoice.Second, answer.Choice);
        Assert.Equal((1, 1), round.BlindScore("C"));
        Assert.Equal(2, round.NextPair!.Index);
        Assert.Null(round.WithAnswer(2, BlindChoice.Skipped).NextPair);
    }

    [Fact]
    public void Par_inexistente_e_configuracao_inexistente_sao_recusados()
    {
        var round = Board();

        Assert.Throws<ArgumentOutOfRangeException>(() => round.WithAnswer(9, BlindChoice.Tie));
        Assert.Throws<ArgumentException>(() => round.WithChosen("Z"));
        Assert.Equal("C", round.WithChosen("C").ChosenConfigId);
    }
}
