using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public class TypographyWidthProfileTests
{
    [Fact]
    public void Serializa_e_le_de_volta_tipografia_e_largura()
    {
        var profile = FormattingProfile.Default with
        {
            Typography = new TypographyRule(QuoteStyle.Ascii, EllipsisStyle.Character, null, '―'),
            Width = new WidthRule(WidthMode.Half),
        };

        var back = FormattingProfileSerializer.Parse(FormattingProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Typography, back.Typography);
        Assert.Equal(profile.Width, back.Width);
    }

    [Fact]
    public void Serializar_recusa_travessao_que_abre_marcador()
    {
        var profile = FormattingProfile.Default with { Typography = new TypographyRule(DialogueDash: '\\') };

        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Serialize(profile));

        Assert.Equal("$.typography.dialogueDash", ex.Field);
    }
}
