using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

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

    [Theory]
    [InlineData("""{ "typography": { "quotes": "curvas" } }""", "$.typography.quotes")]
    [InlineData("""{ "typography": { "dialogueDash": "{" } }""", "$.typography.dialogueDash")]
    [InlineData("""{ "width": { "spaces": "sim" } }""", "$.width.spaces")]
    public void Valor_invalido_aponta_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Serializar_recusa_travessao_que_abre_marcador()
    {
        var profile = FormattingProfile.Default with { Typography = new TypographyRule(DialogueDash: '\\') };

        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Serialize(profile));

        Assert.Equal("$.typography.dialogueDash", ex.Field);
    }
}
