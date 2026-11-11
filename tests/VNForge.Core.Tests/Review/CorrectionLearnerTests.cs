using VNForge.Core.Review;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Review;

public class CorrectionLearnerTests
{
    private static TextUnit Linha(int n, string? falante, string ia, string pessoa, string? traducaoDaIa = null) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", pessoa, TranslationStatus.Translated) { AiTranslation = traducaoDaIa ?? ia };

    private static (string, TextUnit)[] Em(params TextUnit[] linhas) => [.. linhas.Select(l => ("a", l))];

    [Theory]
    [InlineData("Eu gosto do guarda-chuva.", "Eu adoro do guarda-chuva.", "gosto", "adoro")]
    public void Swaps_acha_a_troca_de_palavras(string ia, string pessoa, string de, string para)
    {
        var troca = Assert.Single(CorrectionLearner.Swaps(ia, pessoa));

        Assert.Equal(new WordSwap(de, para), troca);
    }

    [Fact]
    public void Swaps_descarta_a_linha_quando_um_lado_da_troca_passa_do_limite_de_palavras()
    {
        Assert.Empty(CorrectionLearner.Swaps("Ei, vem aqui.", "Um dois três quatro, vem aqui."));
        Assert.Empty(CorrectionLearner.Swaps("Um dois três quatro, vem aqui.", "Ei, vem aqui."));
        Assert.Empty(CorrectionLearner.Swaps("Ei, vem aqui, azul.", "Um dois três quatro, vem aqui, vermelho."));
    }

    [Fact]
    public void Find_oferece_a_troca_por_personagem_no_limite()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "健太", "Ei, vem.", "Ô, vem."),
            Linha(2, "健太", "Ei, anda.", "Ô, anda."),
            Linha(3, "健太", "Ei, olha!", "Ô, olha!"),
            Linha(4, "美咲", "Ei, você.", "Ei, você.")));

        var troca = Assert.Single(achadas);
        Assert.Equal(CorrectionScope.Character, troca.Scope);
        Assert.Equal("健太", troca.Speaker);
        Assert.Equal(new WordSwap("Ei", "Ô"), troca.Swap);
        Assert.Equal(3, troca.Count);
        Assert.Equal(["a:000001", "a:000002", "a:000003"], troca.Lines.Select(l => l.UnitId));
    }

    [Fact]
    public void Find_oferece_para_o_projeto_quando_a_troca_se_espalha_por_personagens_e_narracao()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "健太", "Ele disse tchau.", "Ele falou tchau."),
            Linha(2, "美咲", "Ela disse oi.", "Ela falou oi."),
            Linha(3, null, "Alguém disse não.", "Alguém falou não.")));

        var troca = Assert.Single(achadas);
        Assert.Equal(CorrectionScope.Project, troca.Scope);
        Assert.Null(troca.Speaker);
        Assert.Equal(3, troca.Count);
    }

    [Fact]
    public void Find_oferece_para_o_projeto_quando_nenhum_personagem_concentra_a_troca()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "健太", "Ei, a.", "Ô, a."),
            Linha(2, "健太", "Ei, b.", "Ô, b."),
            Linha(3, "健太", "Ei, c.", "Ô, c."),
            Linha(4, "美咲", "Ei, d.", "Ô, d."),
            Linha(5, "美咲", "Ei, e.", "Ô, e.")));

        var troca = Assert.Single(achadas);
        Assert.Equal(CorrectionScope.Project, troca.Scope);
        Assert.Equal(5, troca.Count);
    }

    [Fact]
    public void Find_ordena_pelas_mais_frequentes_ignora_a_ordem_trocada_e_o_limite_vale()
    {
        var linhas = new List<TextUnit>
        {
            Linha(1, null, "Ei, a.", "Ô, a."), Linha(2, null, "Ei, b.", "Ô, b."), Linha(3, null, "Ei, c.", "Ô, c."),
            Linha(4, null, "Vou já.", "Já vou."), Linha(5, null, "Vou já.", "Já vou."), Linha(6, null, "Vou já.", "Já vou."),
            Linha(7, null, "Eu disse.", "Eu falei."), Linha(8, null, "Eu disse.", "Eu falei."), Linha(9, null, "Eu disse.", "Eu falei."), Linha(10, null, "Eu disse.", "Eu falei."),
        };

        var achadas = CorrectionLearner.Find(Em([.. linhas]));

        Assert.Equal(["disse", "Ei"], achadas.Select(c => c.Swap.From));
        Assert.Equal(["disse"], CorrectionLearner.Find(Em([.. linhas]), threshold: 4).Select(c => c.Swap.From));
        Assert.Throws<ArgumentOutOfRangeException>(() => CorrectionLearner.Find(Em(), threshold: 1));
    }

    [Fact]
    public void A_regra_do_projeto_vai_para_o_style_md_como_item_de_lista_sem_repetir()
    {
        var troca = new RepeatedCorrection(CorrectionScope.Project, null, new WordSwap("Ei", "Ô"), [("a", "a:000001")]);

        Assert.Equal("Use \"Ô\" no lugar de \"Ei\".", troca.StyleLine);
        Assert.Equal("- Use \"Ô\" no lugar de \"Ei\".\n", troca.AddTo(null));
        Assert.Equal("Tom leve.\n- Use \"Ô\" no lugar de \"Ei\".\n", troca.AddTo("Tom leve.\n\n"));
        Assert.True(troca.IsIn("Tom leve.\n- use \"ô\" no lugar de \"ei\"."));
        Assert.Equal("- Use \"Ô\" no lugar de \"Ei\".\n", troca.AddTo("- Use \"Ô\" no lugar de \"Ei\".\n"));
        Assert.False(troca.IsIn(null));
    }

    [Fact]
    public void A_regra_do_personagem_vai_para_o_Como_fala_depois_do_que_ja_esta_escrito()
    {
        var troca = new RepeatedCorrection(CorrectionScope.Character, "健太", new WordSwap("Ei", "Ô"), [("a", "a:000001")]);

        Assert.Equal("diz \"Ô\" no lugar de \"Ei\"", troca.AddTo(null));
        Assert.Equal("direto; diz \"Ô\" no lugar de \"Ei\"", troca.AddTo("direto."));
        Assert.Equal("direto; diz \"Ô\" no lugar de \"Ei\"", troca.AddTo("direto;  "));
        Assert.Equal("direto; diz \"Ô\" no lugar de \"Ei\"", troca.AddTo("direto; diz \"Ô\" no lugar de \"Ei\""));
        Assert.True(troca.IsIn("direto; diz \"Ô\" no lugar de \"Ei\""));
    }
}
