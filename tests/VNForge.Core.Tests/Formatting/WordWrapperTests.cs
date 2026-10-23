using VNForge.Core.Formatting;

namespace VNForge.Core.Tests.Formatting;

public class WordWrapperTests
{
    private static WrapResult Wrap(string text, int width, int? lines = null, CharWidth? widths = null) =>
        WordWrapper.Wrap(text, new WrapOptions(width, lines, widths ?? CharWidths.Uniform));

    [Fact]
    public void Quebra_entre_palavras()
    {
        WrapResult r = Wrap("uma frase de teste", 10);
        Assert.Equal(@"uma frase\nde teste", r.Text);
        Assert.Equal([2], r.LinesPerBox);
    }

    [Fact]
    public void Texto_que_cabe_nao_muda()
    {
        Assert.Equal("curto", Wrap("curto", 10).Text);
    }

    [Fact]
    public void Palavra_maior_que_a_largura_e_cortada_no_limite()
    {
        WrapResult r = Wrap("preparacao", 4);
        Assert.Equal(@"prep\narac\nao", r.Text);
        Assert.Equal([3], r.LinesPerBox);
    }

    [Fact]
    public void Palavra_longa_no_meio_da_linha_comeca_em_linha_nova()
    {
        Assert.Equal(@"oi\nabcd\nefg", Wrap("oi abcdefg", 4).Text);
    }

    [Fact]
    public void Caractere_mais_largo_que_a_linha_nao_gera_laco_infinito()
    {
        CharWidth wide = _ => 5;
        WrapResult r = Wrap("abc", 2, widths: wide);
        Assert.Equal(@"a\nb\nc", r.Text);
    }

    [Fact]
    public void Quebras_existentes_sao_respeitadas()
    {
        WrapResult r = Wrap(@"a\nb c", 10);
        Assert.Equal(@"a\nb c", r.Text);
        Assert.Equal([2], r.LinesPerBox);
    }

    [Fact]
    public void Marcadores_nao_ocupam_coluna_e_nao_sao_partidos()
    {
        WrapResult r = Wrap(@"abcd{VAR:nome_longo}\cefgh", 8);
        Assert.Equal(@"abcd{VAR:nome_longo}\cefgh", r.Text);
        Assert.Equal([1], r.LinesPerBox);
    }

    [Fact]
    public void Marcador_acompanha_a_palavra_na_quebra()
    {
        Assert.Equal(@"abc\n{EF:0a}def", Wrap("abc {EF:0a}def", 4).Text);
    }

    [Fact]
    public void Barra_e_chave_escapadas_contam_uma_coluna()
    {
        Assert.Equal(@"a\\b\n\{c", Wrap(@"a\\b \{c", 3).Text);
    }

    [Fact]
    public void Nova_pagina_zera_a_contagem_de_linhas()
    {
        WrapResult r = Wrap(@"aa bb\paa bb cc", 5, lines: 2);
        Assert.Equal(@"aa bb\paa bb\ncc", r.Text);
        Assert.Equal([1, 2], r.LinesPerBox);
        Assert.False(r.ExceedsLineLimit);
    }

    [Fact]
    public void Texto_acima_do_limite_de_linhas_e_informado()
    {
        WrapResult r = Wrap(@"aa\paa bb cc dd ee", 5, lines: 2);
        Assert.Equal([1, 3], r.LinesPerBox);
        Assert.True(r.ExceedsLineLimit);
        Assert.Equal([1], r.OverflowingBoxes);
    }

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
    public void Letra_acentuada_conta_meia_celula()
    {
        Assert.Equal("preparação", WordWrapper.Wrap("preparação", new WrapOptions(10)).Text);
    }

    [Fact]
    public void Espacos_na_quebra_sao_descartados()
    {
        Assert.Equal(@"abc\ndef", Wrap("abc   def", 4).Text);
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
