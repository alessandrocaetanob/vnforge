using VNForge.Core.Review;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Review;

public class CorrectionLearnerTests
{
    private static TextUnit Linha(int n, string? falante, string ia, string pessoa, string? traducaoDaIa = null) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", pessoa, TranslationStatus.Translated) { AiTranslation = traducaoDaIa ?? ia };

    private static (string, TextUnit)[] Em(params TextUnit[] linhas) => [.. linhas.Select(l => ("a", l))];

    [Theory]
    [InlineData("Ei, vem aqui.", "Ô, vem aqui.", "Ei", "Ô")]
    [InlineData("Ei, vem aqui.", "ô, vem aqui.", "Ei", "ô")]
    [InlineData("Eu gosto do guarda-chuva.", "Eu adoro do guarda-chuva.", "gosto", "adoro")]
    [InlineData("Você vai se molhar.", "Tu vais te molhar.", "Você vai se", "Tu vais te")]
    public void Swaps_acha_a_troca_de_palavras(string ia, string pessoa, string de, string para)
    {
        var troca = Assert.Single(CorrectionLearner.Swaps(ia, pessoa));

        Assert.Equal(new WordSwap(de, para), troca);
    }

    [Theory]
    [InlineData("Ei, vem aqui.", "Ei, vem aqui.")]
    [InlineData("Ei, vem aqui.", "ei, vem aqui.")]
    [InlineData("Ei, vem aqui.", "Ei! Vem aqui?")]
    [InlineData("Ei, vem aqui.", "Ei, vem aqui agora.")]
    [InlineData("Ei, vem aqui agora.", "Ei, vem aqui.")]
    [InlineData(null, "Ei.")]
    [InlineData("Ei.", " ")]
    public void Swaps_ignora_pontuacao_caixa_e_acrescimos_ou_cortes_puros(string? ia, string? pessoa)
    {
        Assert.Empty(CorrectionLearner.Swaps(ia, pessoa));
    }

    [Fact]
    public void Swaps_acha_varias_trocas_na_mesma_linha_na_ordem_do_texto()
    {
        var trocas = CorrectionLearner.Swaps("Ei, você viu o carro azul?", "Ô, você viu o carro vermelho?");

        Assert.Equal([new WordSwap("Ei", "Ô"), new WordSwap("azul", "vermelho")], trocas);
    }

    [Fact]
    public void Swaps_nao_aprende_com_linha_reescrita()
    {
        Assert.Empty(CorrectionLearner.Swaps("Um dois três quatro cinco seis sete oito.", "Cada coisa tem a sua hora marcada no relógio."));
        Assert.Empty(CorrectionLearner.Swaps("a b c d e", "w x y z k"));
    }

    [Theory]
    [InlineData("Ei, você viu o carro azul?", "Ei, você viu o novo carro vermelho?", "azul", "vermelho")]
    [InlineData("Ei, você viu o novo carro azul?", "Ei, você viu o carro vermelho?", "azul", "vermelho")]
    [InlineData("Ei, você viu o carro azul agora?", "Ô, você viu o carro azul?", "Ei", "Ô")]
    public void Swaps_ignora_acrescimo_ou_corte_puro_no_meio_e_acha_a_troca_ao_lado(string ia, string pessoa, string de, string para)
    {
        var troca = CorrectionLearner.Swaps(ia, pessoa);

        Assert.Contains(new WordSwap(de, para), troca);
        Assert.DoesNotContain(troca, t => t.From.Length == 0 || t.To.Length == 0);
    }

    [Fact]
    public void Swaps_descarta_a_linha_quando_um_lado_da_troca_passa_do_limite_de_palavras()
    {
        Assert.Empty(CorrectionLearner.Swaps("Ei, vem aqui.", "Um dois três quatro, vem aqui."));
        Assert.Empty(CorrectionLearner.Swaps("Um dois três quatro, vem aqui.", "Ei, vem aqui."));
        Assert.Empty(CorrectionLearner.Swaps("Ei, vem aqui, azul.", "Um dois três quatro, vem aqui, vermelho."));
    }

    [Fact]
    public void Swaps_aceita_troca_de_tres_palavras_de_cada_lado_e_ignora_a_que_so_muda_a_caixa()
    {
        Assert.Equal([new WordSwap("um dois três", "a b c")], CorrectionLearner.Swaps("Foi um dois três vez.", "Foi a b c vez."));
        Assert.Empty(CorrectionLearner.Swaps("Foi Ei vez.", "Foi ei vez."));
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
    public void Find_nao_oferece_abaixo_do_limite()
    {
        Assert.Empty(CorrectionLearner.Find(Em(Linha(1, "健太", "Ei, vem.", "Ô, vem."), Linha(2, "健太", "Ei, anda.", "Ô, anda."))));
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
    public void Find_prefere_o_personagem_quando_ele_concentra_dois_tercos_da_troca()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "健太", "Ei, a.", "Ô, a."),
            Linha(2, "健太", "Ei, b.", "Ô, b."),
            Linha(3, "健太", "Ei, c.", "Ô, c."),
            Linha(4, "健太", "Ei, d.", "Ô, d."),
            Linha(5, "美咲", "Ei, e.", "Ô, e.")));

        var troca = Assert.Single(achadas);
        Assert.Equal(CorrectionScope.Character, troca.Scope);
        Assert.Equal(4, troca.Count);
    }

    [Fact]
    public void Find_usa_o_falante_escolhido_e_trata_a_troca_sem_diferenciar_caixa()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "美咲", "Ei, a.", "Ô, a.") with { ChosenSpeaker = "健太" },
            Linha(2, "健太", "Vem, ei.", "Vem, ô."),
            Linha(3, "健太", "EI, c.", "Ô, c.")));

        var troca = Assert.Single(achadas);
        Assert.Equal("健太", troca.Speaker);
        Assert.Equal(3, troca.Count);
    }

    [Fact]
    public void Find_conta_a_linha_uma_vez_mesmo_com_a_troca_repetida_nela()
    {
        var achadas = CorrectionLearner.Find(Em(
            Linha(1, "健太", "Ei, ei, ei.", "Ô, ô, ô."),
            Linha(2, "健太", "Ei, b.", "Ô, b.")));

        Assert.Empty(achadas);
    }

    [Fact]
    public void Find_ignora_linha_sem_texto_da_IA_ou_sem_traducao_utilizavel()
    {
        var manual = new TextUnit("a:000001", TextUnitKind.Dialogue, "健太", "x", "Ô, a.", TranslationStatus.Translated);
        var limpa = Linha(2, "健太", "Ei, b.", "Ô, b.") with { Translation = null, Status = TranslationStatus.Untranslated };
        var keep = Linha(3, "健太", "Ei, c.", "Ô, c.") with { Status = TranslationStatus.Keep };

        Assert.Empty(CorrectionLearner.Find(Em(manual, limpa, keep, Linha(4, "健太", "Ei, d.", "Ô, d."))));
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
