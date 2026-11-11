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
    public void Marcador_invalido_no_campo_nao_derruba_a_conferencia()
    {
        Assert.Equal(["esquesi"], Wrong(@"esquesi \ e { sem fim"));
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
    [InlineData("Ahhhh! Hahaha! Kyaaa! Shhh!")]
    public void Interjeicoes_e_onomatopeias_comuns_nao_sao_erro(string text)
    {
        Assert.Empty(Checker.Find(text));
    }
}
