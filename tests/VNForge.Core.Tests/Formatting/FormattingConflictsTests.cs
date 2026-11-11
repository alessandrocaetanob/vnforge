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
}
