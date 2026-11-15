using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
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
}
