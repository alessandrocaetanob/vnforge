using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public class TypographyWidthTests
{
    private static string Format(FormattingProfile profile, string text) => FormattingPipeline.Create(profile).Format(text).Text;

    private static FormattingProfile With(TypographyRule typography) => FormattingProfile.Default with { Typography = typography };

    private static FormattingProfile With(WidthRule width) => FormattingProfile.Default with { Width = width };

    [Fact]
    public void Perfil_padrao_nao_liga_nenhuma_regra_de_tipografia_nem_de_largura()
    {
        const string text = "“Olá...” — disse ‘ele’… Hoje!";

        Assert.Equal(text, Format(FormattingProfile.Default, text));
    }

    [Theory]
    [InlineData(QuoteStyle.Ascii, "\"Olá\" e 'oi'")]
    [InlineData(QuoteStyle.Corner, "「Olá」 e ‘oi’")]
    [InlineData(QuoteStyle.None, "“Olá” e ‘oi’")]
    public void Aspas_curvas_seguem_o_estilo_do_perfil(QuoteStyle style, string expected)
    {
        Assert.Equal(expected, Format(With(new TypographyRule(Quotes: style)), "“Olá” e ‘oi’"));
    }

    [Fact]
    public void Reticencias_ficam_como_estao_com_a_regra_desligada()
    {
        Assert.Equal("Espere... sim…", Format(With(new TypographyRule(Ellipsis: EllipsisStyle.None)), "Espere... sim…"));
    }

    [Fact]
    public void Reticencias_tres_pontos_viram_o_caractere()
    {
        Assert.Equal("Espere… sim… e……", Format(With(new TypographyRule(Ellipsis: EllipsisStyle.Character)), "Espere... sim... e......"));
    }

    [Fact]
    public void Reticencias_caractere_vira_tres_pontos()
    {
        Assert.Equal("Espere... sim", Format(With(new TypographyRule(Ellipsis: EllipsisStyle.Dots)), "Espere… sim"));
    }

    [Fact]
    public void Dois_pontos_e_ponto_final_nao_viram_reticencias()
    {
        Assert.Equal("Fim.. ok", Format(With(new TypographyRule(Ellipsis: EllipsisStyle.Character)), "Fim.. ok"));
    }

    [Fact]
    public void Travessao_vira_o_caractere_do_perfil_em_qualquer_posicao()
    {
        var profile = With(new TypographyRule(Dash: '―'));

        Assert.Equal("Ele ― e ela", Format(profile, "Ele — e ela"));
        Assert.Equal("Ele — e ela", Format(FormattingProfile.Default, "Ele — e ela"));
    }

    [Theory]
    [InlineData("— Olá", "― Olá")]
    [InlineData("– Olá", "― Olá")]
    [InlineData("― Olá", "― Olá")]
    [InlineData("- Olá", "― Olá")]
    [InlineData("—Olá", "―Olá")]
    public void Fala_que_abre_com_travessao_sai_com_o_caractere_do_perfil(string input, string expected)
    {
        Assert.Equal(expected, Format(With(new TypographyRule(DialogueDash: '―')), input));
    }

    [Fact]
    public void Fala_que_abre_com_travessao_fica_como_esta_com_a_regra_desligada()
    {
        Assert.Equal("— Olá", Format(FormattingProfile.Default, "— Olá"));
    }

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
    public void Travessao_geral_e_de_fala_juntos_o_de_fala_vence_no_comeco_da_linha()
    {
        var profile = With(new TypographyRule(Dash: '―', DialogueDash: '─'));

        Assert.Equal("─ Oi ― tchau", Format(profile, "— Oi — tchau"));
    }

    [Fact]
    public void Largura_cheia_troca_ascii_visivel_e_deixa_espaco_e_marcadores()
    {
        var profile = With(new WidthRule(WidthMode.Full));

        Assert.Equal("Ｈｏｊｅ ｅ １２３！", Format(profile, "Hoje e 123!"));
        Assert.Equal(@"\cＯｉ\u {VAR:nome}", Format(profile, @"\cOi\u {VAR:nome}"));
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
    public void Largura_nao_mexe_em_japones()
    {
        Assert.Equal("こんにちは、ＡＢ", Format(With(new WidthRule(WidthMode.Full)), "こんにちは、AB"));
        Assert.Equal("こんにちは、AB", Format(With(new WidthRule(WidthMode.Half)), "こんにちは、ＡＢ"));
    }

    [Fact]
    public void Largura_off_nao_muda_nada()
    {
        Assert.Equal("Hoje ＡＢ", Format(With(new WidthRule(WidthMode.None)), "Hoje ＡＢ"));
    }

    [Fact]
    public void Tipografia_roda_antes_da_largura()
    {
        var profile = FormattingProfile.Default with
        {
            Typography = new TypographyRule(Ellipsis: EllipsisStyle.Dots),
            Width = new WidthRule(WidthMode.Full),
        };

        Assert.Equal("ａ．．．", Format(profile, "a…"));
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
