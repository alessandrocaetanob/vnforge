using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Glossary;

public class CorrectedTermsTests
{
    private static TextUnit Linha(int n, string original, string? ia, string pessoa, TranslationStatus status = TranslationStatus.Translated) =>
        new($"a:{n:D6}", TextUnitKind.Dialogue, null, original, pessoa, status) { AiTranslation = ia };

    // Três falas com 魔法学院, onde a IA escreveu "escola de magia" e a pessoa trocou por "Academia".
    private static List<TextUnit> TresCorrigidas() =>
    [
        Linha(0, "魔法学院に行く", "Vou para a escola de magia hoje.", "Vou para a Academia hoje."),
        Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A Academia é grande."),
        Linha(2, "魔法学院の朝", "Uma manhã na escola de magia.", "Uma manhã na Academia."),
    ];

    [Fact]
    public void Oferece_o_par_quando_o_mesmo_trecho_ganha_a_mesma_traducao_em_varias_falas()
    {
        var pares = RepeatedTerms.Corrected(TresCorrigidas());

        var par = Assert.Single(pares);
        Assert.Equal(new TermPair("魔法学院", "Academia", "escola de magia", 3), par);
    }

    [Fact]
    public void Duas_falas_nao_bastam()
    {
        Assert.Empty(RepeatedTerms.Corrected(TresCorrigidas().Take(2)));
        Assert.Single(RepeatedTerms.Corrected(TresCorrigidas().Take(2), threshold: 2));
    }

    [Fact]
    public void Fala_que_a_pessoa_nao_mudou_nao_conta()
    {
        List<TextUnit> units = [.. TresCorrigidas().Take(2), Linha(3, "魔法学院です", "Escola de magia.", "Escola de magia.")];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Fala_sem_a_traducao_da_ia_guardada_ou_sem_traducao_nao_conta()
    {
        List<TextUnit> units =
        [
            .. TresCorrigidas().Take(2),
            Linha(3, "魔法学院です", null, "É a Academia."),
            Linha(4, "魔法学院です", "É a escola de magia.", "É a Academia.", TranslationStatus.Untranslated),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Traducoes_diferentes_para_o_mesmo_trecho_nao_viram_par()
    {
        List<TextUnit> units =
        [
            Linha(0, "魔法学院に行く", "Vou para a escola de magia hoje.", "Vou para a Academia hoje."),
            Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A Escola é grande."),
            Linha(2, "魔法学院の朝", "Uma manhã na escola de magia.", "Uma manhã no Colégio."),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Troca_que_acontece_em_falas_de_todo_tipo_nao_e_do_trecho()
    {
        // "Ei" por "Ô" é de estilo: nenhum trecho concentra a troca (魔法学院 tem 3 das 8 falas, 勇者 tem 5 das 8).
        List<TextUnit> units =
        [
            Linha(0, "魔法学院に行く", "Ei, vou para lá.", "Ô, vou para lá."),
            Linha(1, "魔法学院は広い", "Ei, é grande.", "Ô, é grande."),
            Linha(2, "魔法学院の朝", "Ei, é de manhã.", "Ô, é de manhã."),
            .. Enumerable.Range(10, 5).Select(n => Linha(n, $"勇者が来た{n}", $"Ei, ele veio {n}.", $"Ô, ele veio {n}.")),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Dois_trechos_que_dividem_as_mesmas_falas_ficam_de_fora_por_ambiguidade()
    {
        List<TextUnit> units =
        [
            Linha(0, "魔法学院と勇者", "A escola de magia e o herói.", "A Academia e o herói."),
            Linha(1, "魔法学院と勇者だ", "A escola de magia, o herói.", "A Academia, o herói."),
            Linha(2, "勇者と魔法学院", "O herói e a escola de magia.", "O herói e a Academia."),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Ordena_pelas_falas_e_desempata_pelo_original()
    {
        List<TextUnit> units =
        [
            .. TresCorrigidas(),
            Linha(3, "魔法学院です", "É a escola de magia.", "É a Academia."),
            Linha(10, "勇者が来た", "O herói chegou.", "O Bravo chegou."),
            Linha(11, "勇者は強い", "O herói é forte.", "O Bravo é forte."),
            Linha(12, "勇者の剣", "A espada do herói.", "A espada do Bravo."),
        ];

        var pares = RepeatedTerms.Corrected(units);

        Assert.Equal(["魔法学院", "勇者"], pares.Select(p => p.Source));
        Assert.Equal([4, 3], pares.Select(p => p.Lines));
    }

    [Fact]
    public void Excluding_tira_o_que_o_glossario_ja_cobre()
    {
        var pares = RepeatedTerms.Corrected(TresCorrigidas());

        Assert.Empty(RepeatedTerms.Excluding(pares, ["魔法学院"]));
        Assert.Single(RepeatedTerms.Excluding(pares, ["勇者", "  "]));
    }

    [Fact]
    public void Compara_o_destino_sem_diferenciar_maiusculas()
    {
        List<TextUnit> units =
        [
            Linha(0, "魔法学院に行く", "Vou para a escola de magia hoje.", "Vou para a Academia hoje."),
            Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A academia é grande."),
            Linha(2, "魔法学院の朝", "Uma manhã na escola de magia.", "Uma manhã na ACADEMIA."),
        ];

        var par = Assert.Single(RepeatedTerms.Corrected(units));

        Assert.Equal("Academia", par.Target);
        Assert.Equal(3, par.Lines);
    }

    [Fact]
    public void Uma_fala_com_a_troca_repetida_conta_uma_vez()
    {
        List<TextUnit> units =
        [
            Linha(0, "魔法学院、魔法学院", "A escola de magia, a escola de magia.", "A Academia, a Academia."),
            Linha(1, "魔法学院は広い", "A escola de magia é grande.", "A Academia é grande."),
        ];

        var par = Assert.Single(RepeatedTerms.Corrected(units, threshold: 2));

        Assert.Equal(2, par.Lines);
    }

    [Fact]
    public void O_trecho_e_escolhido_depois_de_tirar_o_que_o_glossario_ja_cobre()
    {
        // 魔法学院 e 勇者 empatam na troca; com 勇者 já no glossário, a troca é de 魔法学院.
        List<TextUnit> units =
        [
            Linha(0, "魔法学院と勇者", "A escola de magia e o herói.", "A Academia e o herói."),
            Linha(1, "魔法学院と勇者だ", "A escola de magia, o herói.", "A Academia, o herói."),
            Linha(2, "勇者と魔法学院", "O herói e a escola de magia.", "O herói e a Academia."),
        ];

        var concentrated = RepeatedTerms.Concentrated(units);

        Assert.Empty(RepeatedTerms.Offer(concentrated, []));
        var par = Assert.Single(RepeatedTerms.Offer(concentrated, ["勇者"]));
        Assert.Equal(new TermPair("魔法学院", "Academia", "escola de magia", 3), par);
    }

    [Fact]
    public void Nome_conhecido_que_vence_a_troca_nao_esconde_o_trecho_que_sobra()
    {
        // 健太 está em 4 falas e vence a troca; excluído, quem tem a troca é 新入生.
        List<TextUnit> units =
        [
            Linha(0, "健太と新入生", "O novato chegou.", "O calouro chegou."),
            Linha(1, "健太は新入生だ", "O novato é bom.", "O calouro é bom."),
            Linha(2, "新入生と健太", "Um novato aqui.", "Um calouro aqui."),
            Linha(3, "健太が来た", "O novato veio.", "O calouro veio."),
        ];

        var concentrated = RepeatedTerms.Concentrated(units);

        Assert.Equal("健太", Assert.Single(RepeatedTerms.Offer(concentrated, [])).Source);
        var par = Assert.Single(RepeatedTerms.Offer(concentrated, ["健太"]));
        Assert.Equal(new TermPair("新入生", "calouro", "novato", 3), par);
    }

    [Fact]
    public void Trecho_que_vence_em_duas_trocas_da_mesma_fala_e_ambiguo()
    {
        // "Ei" por "Ô" só acontece nestas três falas, então 魔法学院 concentra as duas trocas.
        List<TextUnit> units =
        [
            Linha(0, "魔法学院に行く", "Ei, vou para a escola de magia.", "Ô, vou para a Academia."),
            Linha(1, "魔法学院は広い", "Ei, a escola de magia é grande.", "Ô, a Academia é grande."),
            Linha(2, "魔法学院の朝", "Ei, manhã na escola de magia.", "Ô, manhã na Academia."),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Dois_termos_novos_nas_mesmas_falas_nao_se_separam_e_nenhum_sai()
    {
        // Sem como saber qual trecho levou qual troca: as contagens são idênticas, então nenhum par é oferecido.
        List<TextUnit> units =
        [
            Linha(0, "魔法学院の国王", "O rei da escola de magia.", "O Rei da Academia."),
            Linha(1, "国王と魔法学院", "O rei e a escola de magia.", "O Rei e a Academia."),
            Linha(2, "魔法学院、国王", "A escola de magia, o rei.", "A Academia, o Rei."),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Fala_corrigida_so_na_pontuacao_conta_para_o_trecho()
    {
        // 6 falas corrigidas com 魔法学院, só 3 com a troca: 3 de 6 não chega a dois terços.
        List<TextUnit> units =
        [
            .. TresCorrigidas(),
            Linha(3, "魔法学院です", "É a escola de magia.", "É a escola de magia!"),
            Linha(4, "魔法学院だ", "A escola de magia.", "A escola de magia..."),
            Linha(5, "魔法学院か", "Na escola de magia?", "Na escola de magia!"),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }

    [Fact]
    public void Troca_em_falas_sem_trecho_japones_tambem_conta_como_de_estilo()
    {
        List<TextUnit> units =
        [
            .. TresCorrigidas().Select((u, i) => Linha(i, u.Source, $"Ei, {u.AiTranslation}", $"Ô, {u.AiTranslation}")),
            .. Enumerable.Range(10, 5).Select(n => Linha(n, "ああ、そうだ", "Ei, é isso.", "Ô, é isso.")),
        ];

        Assert.Empty(RepeatedTerms.Corrected(units));
    }
}
