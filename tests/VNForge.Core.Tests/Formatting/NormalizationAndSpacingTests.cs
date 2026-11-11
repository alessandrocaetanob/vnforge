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
    public void Espacos_invisiveis_ligado_remove_e_desligado_deixa()
    {
        const string text = "a​b‌c‍d⁠e﻿f";

        Assert.Equal("abcdef", Format(text, new NormalizationRule(InvisibleSpaces: true)));
        Assert.Equal(text, Format(text, new NormalizationRule(InvisibleSpaces: false)));
    }

    [Theory]
    [InlineData("Fim.Começo", "Fim. Começo")]
    [InlineData("Ah...você", "Ah... você")]
    public void Espaco_depois_da_pontuacao_ligado_insere_e_desligado_deixa(string input, string expected)
    {
        Assert.Equal(expected, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: true)));
        Assert.Equal(input, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: false)));
    }

    [Theory]
    [InlineData("Não...?")]
    public void Espaco_depois_da_pontuacao_nao_mexe_em_numeros_enderecos_e_sequencias(string input)
    {
        Assert.Equal(input, Format(input, spacing: new SpacingRule(SpaceAfterPunctuation: true)));
    }

    [Fact]
    public void Espaco_antes_da_virgula_e_do_ponto_nao_e_tirado()
    {
        Assert.Equal("Ei , você .", Format("Ei , você .", AllNormalization, AllSpacing));
    }

    [Fact]
    public void Marcadores_ficam_onde_estao()
    {
        const string text = @"\cEi,você  \u?{VAR:n} !\nOk  \p";

        Assert.Equal(@"\cEi, você \u?{VAR:n}!\nOk \p", Format(text, AllNormalization, AllSpacing));
    }
}
