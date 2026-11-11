using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class TextQaRulesTests
{
    private static TextUnit Unit(string? translation, string source = "原文", TranslationStatus status = TranslationStatus.Translated) =>
        new("a:1", TextUnitKind.Dialogue, null, source, translation, status);

    private static IReadOnlyList<QaIssue> Run(IQaRule rule, TextUnit unit) =>
        [.. rule.Check(QaContext.ForTexts(new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = [unit] }))];

    [Fact]
    public void Espaco_antes_de_pontuacao_pega_espacos_seguidos_e_varios_sinais()
    {
        Assert.Equal([new QaSpan(1, 3, "  !"), new QaSpan(6, 2, " ;")], SpaceBeforePunctuationRule.Find("a  ! b ;"));
    }

    [Fact]
    public void Parenteses_em_ordem_trocada_apontam_os_dois()
    {
        Assert.Equal(2, Run(new UnbalancedPairsRule(), Unit("Ele )disse( oi")).Count);
    }

    [Fact]
    public void Aspas_desbalanceadas_aponta_o_caractere_sem_par()
    {
        Assert.Equal([new QaSpan(10, 1, "\"")], UnbalancedPairsRule.Find("Ele disse \"oi"));
        Assert.Equal([new QaSpan(12, 1, ")")], UnbalancedPairsRule.Find("Ele disse oi)"));
    }

    [Fact]
    public void Aspas_desbalanceadas_cala_quando_o_original_tem_o_mesmo_desbalanceamento()
    {
        // A fala abre numa linha e fecha na seguinte: o original também está partido.
        Assert.Empty(Run(new UnbalancedPairsRule(), Unit("“Eu vou", source: "「私は行く")));
        Assert.Single(Run(new UnbalancedPairsRule(), Unit("“Eu vou", source: "「私は行く」")));
    }

    // ---- japones-na-traducao

    [Fact]
    public void Japones_na_traducao_dispara_e_aponta_cada_trecho()
    {
        var issues = Run(new LeftoverJapaneseRule(), Unit("Olá, 世界 e ありがとう"));
        Assert.Equal(2, issues.Count);
        var issue = issues[0];

        Assert.Equal("japones-na-traducao", issue.RuleId);
        Assert.Equal(QaSeverity.Warning, issue.Severity);
        Assert.Equal([new QaSpan(5, 2, "世界"), new QaSpan(10, 5, "ありがとう")], LeftoverJapaneseRule.Find("Olá, 世界 e ありがとう"));
    }

    [Fact]
    public void Japones_na_traducao_pega_katakana_inteiro_e_meia_largura()
    {
        Assert.Equal([new QaSpan(0, 3, "カナダ")], LeftoverJapaneseRule.Find("カナダ"));
        Assert.Equal([new QaSpan(0, 4, "ｶﾅﾀﾞ")], LeftoverJapaneseRule.Find("ｶﾅﾀﾞ"));
    }

    [Fact]
    public void Japones_na_traducao_nao_dispara_com_texto_latino_pontuacao_japonesa_ou_marcador()
    {
        Assert.Empty(Run(new LeftoverJapaneseRule(), Unit("Tudo certo, ação é já!")));
        Assert.Empty(LeftoverJapaneseRule.Find("「Olá」、mundo。"));
        Assert.Empty(LeftoverJapaneseRule.Find("Oi {VAR:名前} tchau"));
    }

    // ---- todas

    [Fact]
    public void Regras_de_texto_ignoram_linha_sem_traducao_utilizavel_e_marcador_invalido()
    {
        foreach (var rule in TextQaRules.All)
        {
            Assert.Empty(Run(rule, Unit(null)));
            Assert.Empty(Run(rule, Unit("")));
            Assert.Empty(Run(rule, Unit("a  b ? \"x 日本", status: TranslationStatus.Untranslated)));
            Assert.Empty(Run(rule, Unit(@"a  b ? \x")));
        }
    }
}
