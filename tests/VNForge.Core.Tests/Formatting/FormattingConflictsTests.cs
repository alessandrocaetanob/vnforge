using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

public sealed class FormattingConflictsTests
{
    private static ITextEncoder Encoder(string strategy) => TextEncoderRegistry.Default.Create(strategy);

    private static FormattingProfile Profile(TypographyRule? typography = null, WidthRule? width = null) =>
        FormattingProfile.Default with { Typography = typography ?? new(), Width = width ?? new() };

    [Fact]
    public void Reticencias_como_caractere_conflitam_na_cp1252()
    {
        var conflict = Assert.Single(FormattingConflicts.Find(Profile(new(Ellipsis: EllipsisStyle.Character)), Encoder("cp1252")));

        Assert.Equal("typography.ellipsis", conflict.Field);
        Assert.Equal("character", conflict.Current);
        Assert.Equal("…", conflict.Characters);
        Assert.Equal("Reticências: Caractere …", conflict.Label);
        Assert.Equal("dots", conflict.Safe);
        Assert.Equal("Três pontos", conflict.SafeLabel);
    }

    [Fact]
    public void Aspas_japonesas_conflitam_na_cp1252_e_o_valor_seguro_e_aspas_retas()
    {
        var conflict = Assert.Single(FormattingConflicts.Find(Profile(new(Quotes: QuoteStyle.Corner)), Encoder("cp1252")));

        Assert.Equal("typography.quotes", conflict.Field);
        Assert.Equal("corner", conflict.Current);
        Assert.Equal("ascii", conflict.Safe);
    }

    [Fact]
    public void Travessao_escolhido_que_a_estrategia_nao_grava_conflita_e_o_seguro_e_nenhum()
    {
        var conflict = Assert.Single(FormattingConflicts.Find(Profile(new(Dash: '—')), Encoder("cp1252")));

        Assert.Equal("typography.dash", conflict.Field);
        Assert.Equal("—", conflict.Current);
        Assert.Null(conflict.Safe);
    }

    [Fact]
    public void Travessao_de_fala_vira_hifen_quando_a_estrategia_grava_o_hifen()
    {
        var conflict = Assert.Single(FormattingConflicts.Find(Profile(new(DialogueDash: '–')), Encoder("cp1252")));

        Assert.Equal("typography.dialogueDash", conflict.Field);
        Assert.Equal("-", conflict.Safe);
    }

    [Fact]
    public void Largura_cheia_conflita_na_cp1252()
    {
        var conflict = Assert.Single(FormattingConflicts.Find(Profile(width: new(WidthMode.Full)), Encoder("cp1252")));

        Assert.Equal("width.mode", conflict.Field);
        Assert.Equal("full", conflict.Current);
        Assert.Equal("none", conflict.Safe);
        Assert.Contains('Ａ', conflict.Characters);
    }

    [Fact]
    public void Nenhum_ajuste_conflita_na_sjis_onde_o_caractere_e_gravavel()
    {
        var profile = Profile(new(QuoteStyle.Corner, EllipsisStyle.Character, '―', '―'), new(WidthMode.Full, Spaces: true));

        Assert.Empty(FormattingConflicts.Find(profile, Encoder("sjis")));
    }

    [Fact]
    public void Perfil_padrao_nao_tem_conflito_em_nenhuma_estrategia()
    {
        foreach (var strategy in new[] { "cp1252", "sjis", "fold" })
        {
            Assert.Empty(FormattingConflicts.Find(FormattingProfile.Default, Encoder(strategy)));
        }
    }

    [Fact]
    public void Resolve_devolve_o_perfil_sem_conflito_e_nao_mexe_no_resto()
    {
        var encoder = Encoder("cp1252");
        var profile = Profile(new(QuoteStyle.Corner, EllipsisStyle.Character, '—', '–'), new(WidthMode.Full, Spaces: true));
        profile = profile with { Wrap = new WrapRule(true, 40) };

        var resolved = FormattingConflicts.Resolve(profile, FormattingConflicts.Find(profile, encoder));

        Assert.Empty(FormattingConflicts.Find(resolved, encoder));
        Assert.Equal(QuoteStyle.Ascii, resolved.Typography.Quotes);
        Assert.Equal(EllipsisStyle.Dots, resolved.Typography.Ellipsis);
        Assert.Null(resolved.Typography.Dash);
        Assert.Equal('-', resolved.Typography.DialogueDash);
        Assert.Equal(WidthMode.None, resolved.Width.Mode);
        Assert.Equal(profile.Wrap, resolved.Wrap);
        Assert.Equal(profile.Kinds, resolved.Kinds);
    }

    [Fact]
    public void Resolve_sem_conflito_devolve_o_mesmo_perfil()
    {
        var profile = Profile(new(Ellipsis: EllipsisStyle.Dots));

        Assert.Same(profile, FormattingConflicts.Resolve(profile, []));
    }

    [Fact]
    public void Resolve_troca_so_o_conflito_pedido()
    {
        var encoder = Encoder("cp1252");
        var profile = Profile(new(QuoteStyle.Corner, EllipsisStyle.Character));
        var ellipsis = FormattingConflicts.Find(profile, encoder).Single(c => c.Field == "typography.ellipsis");

        var resolved = FormattingConflicts.Resolve(profile, [ellipsis]);

        Assert.Equal(EllipsisStyle.Dots, resolved.Typography.Ellipsis);
        Assert.Equal(QuoteStyle.Corner, resolved.Typography.Quotes);
    }

    [Fact]
    public void Substituicao_do_usuario_fica_de_fora()
    {
        var profile = Profile() with { Replacements = new ReplacementRules([new Replacement("x", "…")], []) };

        Assert.Empty(FormattingConflicts.Find(profile, Encoder("cp1252")));
    }
}
