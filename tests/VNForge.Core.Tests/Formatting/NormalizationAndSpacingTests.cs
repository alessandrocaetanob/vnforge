using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

public class NormalizationAndSpacingTests
{
    private static string Format(string text, NormalizationRule? normalization = null, SpacingRule? spacing = null) =>
        FormattingPipeline.Create(FormattingProfile.Default with
        {
            Normalization = normalization ?? new NormalizationRule(),
            Spacing = spacing ?? new SpacingRule(),
        }).Format(text).Text;

    private static readonly NormalizationRule AllNormalization = new(true, true, true);

    private static readonly SpacingRule AllSpacing = new(true, true, true);

    [Fact]
    public void Padrao_nao_liga_nenhuma_regra()
    {
        const string text = "a ​b  ,  ?é";

        Assert.Equal(text, FormattingPipeline.Create(FormattingProfile.Default).Format(text).Text);
        Assert.Empty(FormattingPipeline.Create(FormattingProfile.Default).Stages);
    }

    [Fact]
    public void Nfc_ligado_compoe_e_desligado_deixa()
    {
        const string decomposed = "é ã";

        Assert.Equal("é ã", Format(decomposed, new NormalizationRule(Nfc: true)));
        Assert.Equal(decomposed, Format(decomposed, new NormalizationRule(Nfc: false)));
    }

    [Fact]
    public void Espacos_invisiveis_ligado_remove_e_desligado_deixa()
    {
        const string text = "a​b‌c‍d⁠e﻿f";

        Assert.Equal("abcdef", Format(text, new NormalizationRule(InvisibleSpaces: true)));
        Assert.Equal(text, Format(text, new NormalizationRule(InvisibleSpaces: false)));
    }

    [Fact]
    public void Nbsp_ligado_vira_espaco_e_desligado_deixa()
    {
        const string text = "a b c";

        Assert.Equal("a b c", Format(text, new NormalizationRule(Nbsp: true)));
        Assert.Equal(text, Format(text, new NormalizationRule(Nbsp: false)));
    }

    [Fact]
    public void Espacos_duplos_ligado_colapsa_e_desligado_deixa()
    {
        Assert.Equal("a b c", Format("a  b   c", spacing: new SpacingRule(CollapseSpaces: true)));
        Assert.Equal("a  b   c", Format("a  b   c", spacing: new SpacingRule(CollapseSpaces: false)));
    }

    [Theory]
    [InlineData("Ei ?", "Ei?")]
    [InlineData("Nossa !", "Nossa!")]
    [InlineData("Veja :", "Veja:")]
    [InlineData("Sim ;", "Sim;")]
    [InlineData("Ei  ?", "Ei?")]
    public void Espaco_antes_da_pontuacao_ligado_tira_e_desligado_deixa(string input, string expected)
    {
        Assert.Equal(expected, Format(input, spacing: new SpacingRule(SpaceBeforePunctuation: true)));
        Assert.Equal(input, Format(input, spacing: new SpacingRule(SpaceBeforePunctuation: false)));
    }

    [Theory]
    [InlineData("Ei,você", "Ei, você")]
    [InlineData("Sim;não", "Sim; não")]
    [InlineData("Nossa!Que", "Nossa! Que")]
    [InlineData("Quem?Eu", "Quem? Eu")]
    [InlineData("Fim.Começo", "Fim. Começo")]
    [InlineData("Veja:Isso", "Veja: Isso")]
    [InlineData("Ah...você", "Ah... você")]
    public void Espaco_depois_da_pontuacao_ligado_insere_e_desligado_deixa(string input, string expected)
    {
        Assert.Equal(expected, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: true)));
        Assert.Equal(input, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: false)));
    }

    [Theory]
    [InlineData("1,5 e 3.14")]
    [InlineData("10:30")]
    [InlineData("www.site.com")]
    [InlineData("Ei, você?!")]
    [InlineData("Ah...")]
    [InlineData("Não...?")]
    [InlineData("Cuidado!!")]
    public void Espaco_depois_da_pontuacao_nao_mexe_em_numeros_enderecos_e_sequencias(string input)
    {
        Assert.Equal(input, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: true)));
    }

    [Theory]
    [InlineData("Ei, você ?!", "Ei, você?!")]
    [InlineData("Ei,  você  ?!", "Ei, você?!")]
    [InlineData("Ei,você?!", "Ei, você?!")]
    [InlineData("Ah...", "Ah...")]
    [InlineData("Ah ...", "Ah ...")]
    public void Ei_voce_e_reticencias_saem_sem_espaco_a_mais(string input, string expected)
    {
        Assert.Equal(expected, Format(input, AllNormalization, AllSpacing));
    }

    [Fact]
    public void Espaco_antes_da_virgula_e_do_ponto_nao_e_tirado()
    {
        Assert.Equal("Ei , você .", Format("Ei , você .", AllNormalization, AllSpacing));
    }

    [Fact]
    public void Nbsp_antes_da_pontuacao_sai_junto_quando_as_duas_regras_estao_ligadas()
    {
        Assert.Equal("Ei?", Format("Ei ?", AllNormalization, AllSpacing));
    }

    [Fact]
    public void Marcadores_ficam_onde_estao()
    {
        const string text = @"\cEi,você  \u?{VAR:n} !\nOk  \p";

        Assert.Equal(@"\cEi, você \u?{VAR:n}!\nOk \p", Format(text, AllNormalization, AllSpacing));
    }

    [Fact]
    public void Etapas_rodam_normalizacao_antes_dos_espacos()
    {
        var stages = FormattingPipeline.Create(FormattingProfile.Default with { Normalization = AllNormalization, Spacing = AllSpacing }).Stages;

        Assert.Equal(
            [FormattingPhase.Normalization, FormattingPhase.Normalization, FormattingPhase.Normalization, FormattingPhase.Spacing, FormattingPhase.Spacing, FormattingPhase.Spacing],
            stages.Select(s => s.Phase));
    }
}
