using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

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
    public void Largura_padrao_conta_latino_como_um_e_japones_como_dois()
    {
        // "ab" = 2 colunas, "あい" = 4 colunas: não cabem juntos em 5 com espaço (2+1+4 = 7).
        var options = new WrapOptions(5);
        Assert.Equal(@"ab\nあい", WordWrapper.Wrap("ab あい", options).Text);
        Assert.Equal(@"あい\nう", WordWrapper.Wrap("あいう", options).Text);
    }

    [Fact]
    public void Texto_vazio_gera_uma_caixa_vazia()
    {
        WrapResult r = Wrap("", 10, lines: 1);
        Assert.Equal("", r.Text);
        Assert.Equal([0], r.LinesPerBox);
    }

    [Theory]
    [InlineData(@"abc\x")]
    [InlineData(@"abc\")]
    [InlineData("abc{VAR")]
    public void Marcador_invalido_e_recusado(string text)
    {
        Assert.Throws<ArgumentException>(() => Wrap(text, 10));
    }

    [Fact]
    public void Largura_menor_que_um_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap("a", 0));
    }

    [Fact]
    public void Quebra_final_nao_conta_linha_vazia()
    {
        WrapResult r = Wrap(@"ola\n", 10, lines: 1);
        Assert.Equal([1], r.LinesPerBox);
        Assert.False(r.ExceedsLineLimit);
    }

    [Fact]
    public void Limite_de_linhas_invalido_e_recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap("a", 10, lines: 0));
    }
}
