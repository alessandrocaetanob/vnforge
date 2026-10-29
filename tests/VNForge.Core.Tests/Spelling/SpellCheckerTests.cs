using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Spelling;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Spelling;

public sealed class SpellCheckerTests
{
    private static SpellChecker Checker => SpellChecker.Portuguese;

    private static string[] Wrong(string text, ProjectDictionary? project = null) => [.. Checker.Find(text, project).Select(span => span.Text)];

    [Fact]
    public void Palavra_errada_vem_com_o_trecho_exato_na_traducao_inteira()
    {
        var span = Assert.Single(Checker.Find("Eu esquesi a chave."));

        Assert.Equal(new QaSpan(3, 7, "esquesi"), span);
    }

    [Fact]
    public void Texto_certo_com_acentos_e_cedilha_nao_dispara()
    {
        Assert.Empty(Checker.Find("Não sei, mas a imaginação dela é maior que a minha. Você viu o coração?"));
    }

    [Fact]
    public void Acento_esquecido_dispara_inclusive_no_comeco_da_frase()
    {
        Assert.Equal(["Nao", "voce"], Wrong("Nao vi voce ontem."));
    }

    [Fact]
    public void Quebra_de_linha_e_virada_de_pagina_separam_palavras()
    {
        Assert.Empty(Checker.Find(@"Olá\nmundo\pcasa"));
        Assert.Equal(["esquesi"], Wrong(@"Olá\nesquesi\pcasa"));
    }

    [Fact]
    public void Marcador_de_dentro_da_palavra_nao_deixa_o_pedaco_virar_erro()
    {
        Assert.Empty(Checker.Find("O {EF:0a}pedacinh{x}o que sobrou"));
        Assert.Empty(Checker.Find(@"Oi, {name}! \cVermelhoxx\c"));
    }

    [Fact]
    public void Texto_dentro_de_chaves_nao_e_conferido()
    {
        Assert.Empty(Checker.Find("Oi, {nome_do_jogador}!"));
    }

    [Fact]
    public void Marcador_invalido_no_campo_nao_derruba_a_conferencia()
    {
        Assert.Equal(["esquesi"], Wrong(@"esquesi \ e { sem fim"));
    }

    [Fact]
    public void Nome_com_inicial_maiuscula_no_meio_da_frase_nao_e_erro()
    {
        Assert.Empty(Checker.Find("Eu vi o Zorgblat ontem."));
        Assert.Empty(Checker.Find("Ela chamou o Takeshi-san."));
    }

    [Fact]
    public void Nome_no_comeco_da_frase_so_passa_se_o_projeto_o_conhece()
    {
        Assert.Equal(["Zorgblat"], Wrong("Zorgblat chegou cedo."));

        var project = ProjectDictionary.Create([new GlossaryTerm("ゾーグ", "Zorgblat")], []);
        Assert.Empty(Checker.Find("Zorgblat chegou cedo.", project));
    }

    [Fact]
    public void Nome_de_personagem_e_palavras_do_glossario_entram_no_dicionario_do_projeto()
    {
        var project = ProjectDictionary.Create(
            [new GlossaryTerm("魔導書", "grimório arcano de Vhalor")],
            [new CharacterEntry("花子", "Hanakoi")]);

        Assert.True(project.Contains("hanakoi"));
        Assert.True(project.Contains("Vhalor"));
        Assert.Empty(Checker.Find("Hanakoi abriu o Vhalor.", project));
        Assert.Equal(["Hanakoi"], Wrong("Hanakoi abriu o livro."));
    }

    [Fact]
    public void Honorifico_japones_depois_de_hifen_nao_e_erro()
    {
        var project = ProjectDictionary.Create([], [new CharacterEntry("拓海", "Takumi")]);

        Assert.Empty(Checker.Find("Takumi-kun, venha. Obrigado, Hanako-chan. Sim, sensei.", project));
    }

    [Fact]
    public void Nome_com_honorifico_e_nome_ate_no_comeco_da_frase()
    {
        Assert.Empty(Checker.Find("Misaki-chan, espere! Zorgblat-sama chegou."));
        Assert.Equal(["Zorgblat"], Wrong("Zorgblat chegou, Misaki-chan."));
    }

    [Fact]
    public void Gaguejo_com_hifen_nao_e_erro()
    {
        Assert.Empty(Checker.Find("S-sim, eu... Q-quê? A-ah!"));
    }

    [Fact]
    public void Alongamento_sigla_e_letra_solta_nao_sao_erro()
    {
        Assert.Empty(Checker.Find("Nããão! AAAH! Ahhh... O OK, a 3D, 5x e b. Ok, tudo bem."));
    }

    [Fact]
    public void Palavra_composta_com_hifen_confere_pelo_dicionario()
    {
        Assert.Empty(Checker.Find("O guarda-chuva e o beija-flor."));
        Assert.Equal(["guarda-chuvva"], Wrong("O guarda-chuvva caiu."));
    }

    [Fact]
    public void Japones_nao_e_conferido_aqui()
    {
        Assert.Empty(Checker.Find("Disse 「こんにちは」 e partiu."));
    }

    [Fact]
    public void Apostrofo_tipografico_vale_como_o_reto()
    {
        Assert.Empty(Checker.Find("A gota d’água caiu."));
        Assert.Equal(["d’águaa"], Wrong("A gota d’águaa caiu."));
    }

    [Fact]
    public void Varias_ocorrencias_saem_na_ordem_do_texto()
    {
        Assert.Equal(["xzzq", "esquesi"], Wrong("O xzzq e o esquesi."));
    }

    [Fact]
    public void Dicionario_de_projeto_e_so_do_projeto()
    {
        var project = ProjectDictionary.Create([new GlossaryTerm("a", "xzzq")], []);

        Assert.Empty(Checker.Find("O xzzq chegou.", project));
        Assert.Equal(["xzzq"], Wrong("O xzzq chegou."));
    }

    [Fact]
    public void Regra_sem_traducao_utilizavel_nao_dispara()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated);

        var kept = new TextUnit("a:2", TextUnitKind.Dialogue, null, "原文", "esquesi", TranslationStatus.Keep);

        Assert.Empty(new SpellingRule().Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit, kept] })));
    }

    [Fact]
    public void Regra_aponta_a_palavra_com_gravidade_de_aviso_e_usa_o_glossario()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "Hanakoi esquesi a chave.", TranslationStatus.Translated);
        var context = new QaContext(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] },
            [],
            [new CharacterEntry("花子", "Hanakoi")],
            FormattingProfile.Default);

        var issue = Assert.Single(new SpellingRule().Check(context));

        Assert.Equal("ortografia", issue.RuleId);
        Assert.Equal(QaSeverity.Warning, issue.Severity);
        Assert.Equal("a:1", issue.UnitId);
        Assert.Contains("esquesi", issue.Message);
        Assert.Contains("posição 8", issue.Message);
    }

    [Fact]
    public void Regra_entra_no_qa_padrao_e_na_categoria_de_ortografia()
    {
        Assert.NotNull(QaEngine.Default.Find("ortografia"));
        Assert.Equal(QaCategories.Spelling, QaCategories.Of("ortografia"));
        Assert.Contains(QaCategories.Spelling, QaCategories.All);
    }

    [Fact]
    public void Aviso_de_ortografia_e_sugestao_e_nao_trava_o_build()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "Eita, kyaa! Isso é um ramen.", TranslationStatus.Translated);
        var context = QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] });
        var report = new QaReport([.. new SpellingRule().Check(context)], [], []);

        Assert.NotEmpty(report.Issues);
        Assert.False(report.Blocks(allowWarnings: false));
        Assert.Empty(report.BlockingIssues);
        Assert.Equal(report.Issues.Count, report.StyleAdviceCount);
    }

    [Theory]
    [InlineData("Sr. Tanaka chegou.")]
    [InlineData("Olá, Sra. Zorgblat.")]
    [InlineData("Dr. Takeshi chegou. Prof. Zorgblat, Profa. Zorgblat e Srta. Zorgblat também.")]
    [InlineData("Ele disse: Takeshi chegou.")]
    [InlineData("Eu... Kenji?")]
    [InlineData("Eu… Kenji?")]
    public void Nome_depois_de_abreviacao_dois_pontos_ou_reticencias_nao_e_erro(string text)
    {
        Assert.Empty(Checker.Find(text));
    }

    [Fact]
    public void Ponto_final_comum_ainda_comeca_frase()
    {
        Assert.Equal(["Zorgblat"], Wrong("Ele saiu. Zorgblat chegou."));
        Assert.Equal(["Nao"], Wrong("Sr. Silva. Nao vi."));
    }

    [Fact]
    public void Dicionario_ilegivel_desliga_a_regra_com_uma_mensagem_e_nao_derruba_a_conferencia()
    {
        var unit = new TextUnit("a:1", TextUnitKind.Dialogue, null, "原文", "Oi.", TranslationStatus.Translated);
        var context = QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] });
        var rule = new SpellingRule(() => throw new VNForgeException("arquivo corrompido"));

        var issue = Assert.Single(rule.Check(context));

        Assert.Equal("a:1", issue.UnitId);
        Assert.Contains("desligada", issue.Message);
        Assert.Contains("arquivo corrompido", issue.Message);
        Assert.False(new QaReport([issue], [], []).Blocks(allowWarnings: false));
    }

    [Theory]
    [InlineData("Ahh, Ohh, Haha, Hehe, Hihi, Kyaa!")]
    [InlineData("Shh. Uhm. Huh? Aham. Uhum.")]
    [InlineData("Ahhhh! Hahaha! Kyaaa! Shhh!")]
    public void Interjeicoes_e_onomatopeias_comuns_nao_sao_erro(string text)
    {
        Assert.Empty(Checker.Find(text));
    }
}
