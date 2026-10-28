using VNForge.Core.Qa;

namespace VNForge.Core.Tests.Qa;

public sealed class TextDiffTests
{
    private static string Join(IReadOnlyList<TextDiffSegment> segments, params TextDiffKind[] kinds) =>
        string.Concat(segments.Where(s => kinds.Contains(s.Kind)).Select(s => s.Text));

    [Fact]
    public void Textos_iguais_viram_um_trecho_so_sem_diferenca()
    {
        var diff = TextDiff.Compare("Bom dia.", "Bom dia.");

        var segment = Assert.Single(diff);
        Assert.Equal(("Bom dia.", TextDiffKind.Same), (segment.Text, segment.Kind));
    }

    [Fact]
    public void Ponto_final_a_mais_aparece_como_trecho_acrescentado()
    {
        var diff = TextDiff.Compare("Bom dia", "Bom dia.");

        Assert.Equal([new TextDiffSegment("Bom dia", TextDiffKind.Same), new TextDiffSegment(".", TextDiffKind.Added)], diff);
    }

    [Fact]
    public void Virgula_que_falta_aparece_como_trecho_faltando_na_posicao_dela()
    {
        var diff = TextDiff.Compare("Antes, Mayumi saiu", "Antes Mayumi saiu");

        Assert.Equal(
            [new TextDiffSegment("Antes", TextDiffKind.Same), new TextDiffSegment(",", TextDiffKind.Missing), new TextDiffSegment(" Mayumi saiu", TextDiffKind.Same)],
            diff);
    }

    [Fact]
    public void Espaco_duplo_aparece_como_espaco_acrescentado()
    {
        var diff = TextDiff.Compare("Oi mundo", "Oi  mundo");

        var added = Assert.Single(diff, s => s.Kind == TextDiffKind.Added);
        Assert.Equal(" ", added.Text);
        Assert.Equal("Oi  mundo", Join(diff, TextDiffKind.Same, TextDiffKind.Added));
    }

    [Fact]
    public void Trocar_aspas_retas_por_tipograficas_mostra_o_que_saiu_e_o_que_entrou()
    {
        var diff = TextDiff.Compare("Disse \"oi\"", "Disse “oi”");

        Assert.Equal("Disse \"oi\"", Join(diff, TextDiffKind.Same, TextDiffKind.Missing));
        Assert.Equal("Disse “oi”", Join(diff, TextDiffKind.Same, TextDiffKind.Added));
        Assert.Contains(diff, s => s is { Kind: TextDiffKind.Added, Text: "“" });
        Assert.Contains(diff, s => s is { Kind: TextDiffKind.Missing, Text: "\"" });
    }

    [Theory]
    [InlineData("Antes de ir à sala, Mayumi passou.", "Antes de ir à sala Mayumi passou")]
    [InlineData("a b c d", "a  b c")]
    [InlineData("", "algo")]
    [InlineData("algo", "")]
    [InlineData("Olá", "Oi")]
    [InlineData("😀 oi", "😀 tchau")]
    public void Referencia_e_variante_sempre_podem_ser_remontadas_pelos_trechos(string reference, string variant)
    {
        var diff = TextDiff.Compare(reference, variant);

        Assert.Equal(reference, Join(diff, TextDiffKind.Same, TextDiffKind.Missing));
        Assert.Equal(variant, Join(diff, TextDiffKind.Same, TextDiffKind.Added));
    }

    [Fact]
    public void Texto_enorme_ainda_remonta_sem_estourar_o_calculo()
    {
        var reference = string.Concat(Enumerable.Repeat("abcdefghij", 600));
        var variant = string.Concat(Enumerable.Repeat("jihgfedcba", 600));

        var diff = TextDiff.Compare(reference, variant);

        Assert.Equal(reference, Join(diff, TextDiffKind.Same, TextDiffKind.Missing));
        Assert.Equal(variant, Join(diff, TextDiffKind.Same, TextDiffKind.Added));
    }

    [Fact]
    public void Trechos_trocados_nao_partem_um_par_substituto_ao_meio()
    {
        var diff = TextDiff.Compare("x😀y", "x😁y");

        Assert.All(diff, s => Assert.DoesNotContain(s.Text, c => char.IsSurrogate(c) && s.Text.Length == 1));
        Assert.Contains(diff, s => s is { Kind: TextDiffKind.Added, Text: "😁" });
    }

    [Fact]
    public void Letra_isolada_igual_entre_duas_trocas_nao_fragmenta_a_marcacao()
    {
        var diff = TextDiff.Compare("casa", "mesa");

        Assert.Equal("casa", Join(diff, TextDiffKind.Same, TextDiffKind.Missing));
        Assert.Equal("mesa", Join(diff, TextDiffKind.Same, TextDiffKind.Added));
        Assert.True(diff.Count <= 4, "A troca deveria virar um bloco de faltando e acrescentado, sem trechos de uma letra.");
    }

    [Theory]
    [InlineData(" ", "·")]
    [InlineData("  ", "··")]
    [InlineData("\t", "→")]
    [InlineData(" ", "°")]
    [InlineData("　", "□")]
    [InlineData(",", ",")]
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
}
