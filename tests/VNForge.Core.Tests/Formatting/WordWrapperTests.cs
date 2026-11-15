using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

[Trait("Risco", "R4")]
public class WordWrapperTests
{
    private static WrapResult Wrap(string text, int width, int? lines = null, CharWidth? widths = null) =>
        WordWrapper.Wrap(text, new WrapOptions(width, lines, widths ?? CharWidths.Uniform));

    [Fact]
    public void Sem_limite_de_linhas_nunca_excede()
    {
        Assert.False(Wrap("a b c d e f g h", 1).ExceedsLineLimit);
    }

    [Fact]
    public void Texto_vazio_gera_uma_caixa_vazia()
    {
        WrapResult r = Wrap("", 10, lines: 1);
        Assert.Equal("", r.Text);
        Assert.Equal([0], r.LinesPerBox);
    }

    [Fact]
    public void Limite_de_linhas_invalido_e_recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap("a", 10, lines: 0));
    }
}
