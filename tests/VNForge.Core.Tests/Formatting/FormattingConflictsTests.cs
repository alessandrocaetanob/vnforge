using VNForge.Core.Formatting;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
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
}
