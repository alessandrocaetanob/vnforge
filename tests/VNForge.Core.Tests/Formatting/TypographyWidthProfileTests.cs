using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

public class TypographyWidthProfileTests
{
    [Fact]
    public void Sem_os_campos_tipografia_e_largura_ficam_desligadas()
    {
        var profile = FormattingProfileSerializer.Parse("{}");

        Assert.Equal(new TypographyRule(), profile.Typography);
        Assert.Equal(new WidthRule(), profile.Width);
    }

    [Fact]
    public void Le_todos_os_campos_de_tipografia_e_largura()
    {
        var profile = FormattingProfileSerializer.Parse("""
            {
              "typography": { "quotes": "corner", "ellipsis": "dots", "dash": "―", "dialogueDash": "─" },
              "width": { "mode": "full", "spaces": true }
            }
            """);

        Assert.Equal(new TypographyRule(QuoteStyle.Corner, EllipsisStyle.Dots, '―', '─'), profile.Typography);
        Assert.Equal(new WidthRule(WidthMode.Full, true), profile.Width);
    }

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
    [InlineData("""{ "typography": { "ellipsis": 3 } }""", "$.typography.ellipsis")]
    [InlineData("""{ "typography": { "dash": "--" } }""", "$.typography.dash")]
    [InlineData("""{ "typography": { "dialogueDash": "" } }""", "$.typography.dialogueDash")]
    [InlineData("""{ "typography": { "dialogueDash": 5 } }""", "$.typography.dialogueDash")]
    [InlineData("""{ "typography": { "dash": "\\" } }""", "$.typography.dash")]
    [InlineData("""{ "typography": { "dialogueDash": "{" } }""", "$.typography.dialogueDash")]
    [InlineData("""{ "typography": { "outro": 1 } }""", "$.typography.outro")]
    [InlineData("""{ "width": { "mode": "cheia" } }""", "$.width.mode")]
    [InlineData("""{ "width": { "spaces": "sim" } }""", "$.width.spaces")]
    [InlineData("""{ "width": [] }""", "$.width")]
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

    [Fact]
    public void Null_desliga_o_travessao()
    {
        var profile = FormattingProfileSerializer.Parse("""{ "typography": { "dash": null, "dialogueDash": null } }""");

        Assert.Null(profile.Typography.Dash);
        Assert.Null(profile.Typography.DialogueDash);
    }
}
