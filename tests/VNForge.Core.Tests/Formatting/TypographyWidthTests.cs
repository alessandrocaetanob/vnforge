using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public class TypographyWidthTests
{
    private static string Format(FormattingProfile profile, string text) => FormattingPipeline.Create(profile).Format(text).Text;

    private static FormattingProfile With(TypographyRule typography) => FormattingProfile.Default with { Typography = typography };

    private static FormattingProfile With(WidthRule width) => FormattingProfile.Default with { Width = width };

    [Fact]
    public void Largura_nao_cria_marcador_com_barra_ou_chave_de_largura_cheia()
    {
        var profile = With(new WidthRule(WidthMode.Half));

        Assert.Equal("＼ ｛", Format(profile, "＼ ｛"));
        Assert.Equal(@"ａ \\ ｂ", Format(With(new WidthRule(WidthMode.Full)), @"a \\ b"));
    }
}
