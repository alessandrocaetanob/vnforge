using VNForge.Core.Jmdict;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>A consulta de uma palavra sob o mouse (#538): acha a palavra num texto sem espaços, tira a conjugação e escolhe a entrada mais comum. Sem rede: só o índice local.</summary>
public sealed class JmdictLookupTests
{
    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    private static readonly JmdictLookup Lookup = new(SyntheticJmdict.Index());

    [Theory]
    [InlineData("刻", "遅刻")]
    public void Acha_a_palavra_sob_qualquer_caractere_dela_numa_frase_sem_espacos(string alvo, string palavra)
    {
        var posicao = Frase.IndexOf(alvo, Frase.IndexOf("二人", StringComparison.Ordinal), StringComparison.Ordinal);

        var achada = Lookup.FindWordAt(Frase, posicao);

        Assert.NotNull(achada);
        Assert.Equal(palavra, achada.Word);
        Assert.Equal(palavra, Frase.Substring(achada.Start, achada.Length));
        Assert.False(achada.IsInflected);
    }

    [Fact]
    public void Sobre_a_pontuacao_ou_o_que_o_dicionario_nao_tem_nao_acha_nada()
    {
        Assert.Null(Lookup.FindWordAt(Frase, Frase.IndexOf('「')));
        Assert.Null(Lookup.FindWordAt(Frase, Frase.IndexOf('。')));
        Assert.Null(Lookup.FindWordAt(Frase, Frase.IndexOf('相')));
        Assert.Null(Lookup.FindWordAt(Frase, -1));
        Assert.Null(Lookup.FindWordAt(Frase, Frase.Length));
        Assert.Null(Lookup.FindWordAt(string.Empty, 0));
    }

    [Fact]
    public void Entrada_traz_o_necessario_para_a_tela()
    {
        var achada = Lookup.FindWordAt("遅刻", 0);

        Assert.NotNull(achada);
        Assert.Equal(["n", "vs"], achada.Main.PartsOfSpeech);
        Assert.Equal(["ちこく"], achada.Main.Readings);
        Assert.Equal(2, achada.Main.Senses.Count);
    }

    [Theory]
    [InlineData("美しかった", "美しい", "passada")]
    [InlineData("来た", "来る", "passada")]
    [InlineData("した", "する", "passada")]
    public void Tira_a_conjugacao_e_diz_qual_era(string conjugada, string palavra, string razao)
    {
        var achada = Lookup.FindWordAt(conjugada, 0);

        Assert.NotNull(achada);
        Assert.Equal(palavra, achada.Word);
        Assert.Equal(conjugada.Length, achada.Length);
        Assert.True(achada.IsInflected);
        Assert.Contains(razao, achada.Inflection);
    }

    [Fact]
    public void Conjugacao_encadeada_lista_as_formas_na_ordem_da_gramatica()
    {
        var achada = Lookup.FindWordAt("食べなかった", 0);

        Assert.NotNull(achada);
        Assert.Equal("食べる", achada.Word);
        Assert.Equal(["negativa", "passada"], achada.Inflection);

        var cortesia = Lookup.FindWordAt("行きませんでした", 0);
        Assert.NotNull(cortesia);
        Assert.Equal("行く", cortesia.Word);
        Assert.Contains("negativa", cortesia.Inflection);
    }

    [Fact]
    public void Consulta_da_palavra_inteira_para_a_linha_de_comando()
    {
        Assert.Equal("行く", Lookup.Find("行こう")?.Word);
        Assert.Equal("遅刻", Lookup.Find("ちこく")?.Main.Kanji[0]);
        Assert.Null(Lookup.Find("遅刻だ"));
        Assert.Null(Lookup.Find("  "));
    }

    [Fact]
    public void Varre_a_frase_inteira_da_esquerda_para_a_direita_sem_sobrepor_palavras()
    {
        var palavras = Lookup.Scan(Frase);

        Assert.Equal(["二人", "そろう", "五分", "遅刻", "だ"], palavras.Select(p => p.Word));
        Assert.All(palavras, p => Assert.True(p.Length > 0));
        Assert.Equal(["二人", "そろって", "五分", "遅刻", "だ"], palavras.Select(p => Frase.Substring(p.Start, p.Length)));
        Assert.Empty(Lookup.Scan("「。」"));
    }
}
