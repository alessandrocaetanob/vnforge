using VNForge.Core.Formatting;
using VNForge.Core.Qa;

namespace VNForge.Core.Tests.Qa;

public sealed class TextDiffTests
{
    private static string Join(IReadOnlyList<TextDiffSegment> segments, params TextDiffKind[] kinds) =>
        string.Concat(segments.Where(s => kinds.Contains(s.Kind)).Select(s => s.Text));

    [Theory]
    [InlineData("Antes de ir à sala, Mayumi passou.", "Antes de ir à sala Mayumi passou")]
    public void Referencia_e_variante_sempre_podem_ser_remontadas_pelos_trechos(string reference, string variant)
    {
        var diff = TextDiff.Compare(reference, variant);

        Assert.Equal(reference, Join(diff, TextDiffKind.Same, TextDiffKind.Missing));
        Assert.Equal(variant, Join(diff, TextDiffKind.Same, TextDiffKind.Added));
    }

    [Theory]
    [InlineData("\t", "→")]
    [InlineData(" ", "°")]
    [InlineData("　", "□")]
    [InlineData("a b", "a·b")]
    public void Visivel_troca_espacos_por_simbolos_que_aparecem_na_tela(string text, string expected)
    {
        Assert.Equal(expected, TextDiff.Visible(text));
    }

    [Fact]
    public void Visivel_do_trecho_usa_o_mesmo_mapa()
    {
        Assert.Equal("··", new TextDiffSegment("  ", TextDiffKind.Added).Visible);
    }

    [Fact]
    public void Texto_com_marcador_invalido_compara_por_caractere_sem_falhar()
    {
        var diff = TextDiff.Compare(@"a\", @"a\p");

        Assert.Equal(@"a\p", Join(diff, TextDiffKind.Same, TextDiffKind.Added));
    }
}
