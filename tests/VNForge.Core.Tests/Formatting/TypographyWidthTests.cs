using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public class TypographyWidthTests
{
    private static string Format(FormattingProfile profile, string text) => FormattingPipeline.Create(profile).Format(text).Text;

    private static FormattingProfile With(TypographyRule typography) => FormattingProfile.Default with { Typography = typography };

    private static FormattingProfile With(WidthRule width) => FormattingProfile.Default with { Width = width };

    [Fact]
    public void Travessao_de_fala_usa_o_caractere_escolhido_e_so_no_comeco_da_linha()
    {
        var profile = With(new TypographyRule(DialogueDash: '─'));

        Assert.Equal(@"─ Olá — tudo bem?\n─ Sim - e você?\p─ Ok", Format(profile, @"— Olá — tudo bem?\n— Sim - e você?\p- Ok"));
    }

    [Fact]
    public void Travessao_de_fala_vale_depois_de_marcador_de_cor_e_hifen_no_meio_da_palavra_fica()
    {
        var profile = With(new TypographyRule(DialogueDash: '―'));

        Assert.Equal(@"\c― Olá\u guarda-chuva", Format(profile, @"\c— Olá\u guarda-chuva"));
        Assert.Equal("-1 e -x", Format(profile, "-1 e -x"));
    }

    [Fact]
    public void Largura_cheia_com_espacos_troca_tambem_o_espaco()
    {
        Assert.Equal("Ｈｏｊｅ　ｅ", Format(With(new WidthRule(WidthMode.Full, Spaces: true)), "Hoje e"));
    }

    [Fact]
    public void Largura_meia_volta_ao_ascii()
    {
        var profile = With(new WidthRule(WidthMode.Half));

        Assert.Equal("Hoje e 123!", Format(profile, "Ｈｏｊｅ ｅ １２３！"));
        Assert.Equal("Hoje　e", Format(profile, "Hoje　e"));
        Assert.Equal("Hoje e", Format(With(new WidthRule(WidthMode.Half, Spaces: true)), "Hoje　e"));
    }

    [Fact]
    public void Largura_nao_cria_marcador_com_barra_ou_chave_de_largura_cheia()
    {
        var profile = With(new WidthRule(WidthMode.Half));

        Assert.Equal("＼ ｛", Format(profile, "＼ ｛"));
        Assert.Equal(@"ａ \\ ｂ", Format(With(new WidthRule(WidthMode.Full)), @"a \\ b"));
    }

    [Fact]
    public void Saida_das_regras_cabe_na_estrategia_fold()
    {
        var profile = FormattingProfile.Default with
        {
            Typography = new TypographyRule(Quotes: QuoteStyle.Corner, Ellipsis: EllipsisStyle.Character, Dash: '―', DialogueDash: '―'),
            Width = new WidthRule(WidthMode.Full),
        };

        var formatted = Format(profile, "— “Ola... tudo — bem?” Hoje");

        Assert.Empty(CharacterFolder.Fold(formatted).Unresolved);
        Assert.Equal(formatted, CharacterFolder.Fold(formatted).Text);
    }
}
