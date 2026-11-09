using VNForge.Core.Jmdict;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>A consulta de uma palavra sob o mouse (#538): acha a palavra num texto sem espaços, tira a conjugação e escolhe a entrada mais comum. Sem rede: só o índice local.</summary>
public sealed class JmdictLookupTests
{
    private const string Frase = "「相沢、佐倉。二人そろって五分遅刻だ」";

    private static readonly JmdictLookup Lookup = new(SyntheticJmdict.Index());

    [Theory]
    [InlineData("遅", "遅刻")]
    [InlineData("刻", "遅刻")]
    [InlineData("二", "二人")]
    [InlineData("人", "二人")]
    [InlineData("五", "五分")]
    [InlineData("だ", "だ")]
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
    public void A_palavra_nao_atravessa_a_pontuacao_nem_o_marcador_do_jogo()
    {
        Assert.Null(Lookup.FindWordAt("遅。刻", 0));
        Assert.Null(Lookup.FindWordAt("二￼人", 0));
    }

    [Fact]
    public void Fica_com_a_palavra_mais_longa_que_cobre_o_caractere()
    {
        var achada = Lookup.FindWordAt("傘学校", 1);

        Assert.NotNull(achada);
        Assert.Equal("学校", achada.Word);
        Assert.Equal(1, achada.Start);
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
    [InlineData("行こう", "行く", "volitiva")]
    [InlineData("行った", "行く", "passada")]
    [InlineData("行って", "行く", "em て")]
    [InlineData("行かない", "行く", "negativa")]
    [InlineData("行きます", "行く", "de cortesia")]
    [InlineData("行けば", "行く", "condicional")]
    [InlineData("食べた", "食べる", "passada")]
    [InlineData("食べない", "食べる", "negativa")]
    [InlineData("食べて", "食べる", "em て")]
    [InlineData("食べます", "食べる", "de cortesia")]
    [InlineData("食べよう", "食べる", "volitiva")]
    [InlineData("食べられる", "食べる", "potencial ou passiva")]
    [InlineData("食べさせる", "食べる", "causativa")]
    [InlineData("美しかった", "美しい", "passada")]
    [InlineData("美しくない", "美しい", "negativa")]
    [InlineData("美しく", "美しい", "adverbial")]
    [InlineData("そろって", "そろう", "em て")]
    [InlineData("揃った", "揃う", "passada")]
    [InlineData("来た", "来る", "passada")]
    [InlineData("来ない", "来る", "negativa")]
    [InlineData("きます", "くる", "de cortesia")]
    [InlineData("した", "する", "passada")]
    [InlineData("しない", "する", "negativa")]
    [InlineData("します", "する", "de cortesia")]
    public void Tira_a_conjugacao_e_diz_qual_era(string conjugada, string palavra, string razao)
    {
        var achada = Lookup.FindWordAt(conjugada, 0);

        Assert.NotNull(achada);
        Assert.Equal(palavra, achada.Word);
        Assert.Equal(conjugada.Length, achada.Length);
        Assert.True(achada.IsInflected);
        Assert.Contains(razao, achada.Inflection);
    }

    [Theory]
    [InlineData("食べたら", "食べる", "condicional (たら)")]
    [InlineData("行ったら", "行く", "condicional (たら)")]
    [InlineData("美しかったら", "美しい", "condicional (たら)")]
    [InlineData("したら", "する", "condicional (たら)")]
    [InlineData("食べている", "食べる", "contínua")]
    [InlineData("食べてる", "食べる", "contínua")]
    [InlineData("食べていた", "食べる", "contínua")]
    [InlineData("行っている", "行く", "contínua")]
    [InlineData("揃ってる", "揃う", "contínua")]
    [InlineData("食べちゃう", "食べる", "completa (ちゃう)")]
    [InlineData("食べちゃった", "食べる", "completa (ちゃう)")]
    [InlineData("食べず", "食べる", "negativa (ず)")]
    [InlineData("行かず", "行く", "negativa (ず)")]
    [InlineData("せず", "する", "negativa (ず)")]
    public void Tira_tambem_o_tara_o_teiru_o_chau_e_o_zu(string conjugada, string palavra, string razao)
    {
        var achada = Lookup.FindWordAt(conjugada, 0);

        Assert.NotNull(achada);
        Assert.Equal(palavra, achada.Word);
        Assert.Equal(conjugada.Length, achada.Length);
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
    public void Conjugacao_so_vale_para_a_classe_certa_da_entrada()
    {
        // 傘 é substantivo: "傘た" não vira "傘る", e "遅刻た" não é palavra.
        Assert.Equal("傘", Lookup.FindWordAt("傘た", 0)?.Word);
        Assert.Equal(1, Lookup.FindWordAt("傘た", 0)?.Length);
    }

    [Fact]
    public void Forma_ambigua_mostra_primeiro_a_entrada_mais_comum()
    {
        // "行って" é 行く (nf10) e também 行う (nf40): a mais comum vai primeiro, a outra segue na lista.
        var achada = Lookup.FindWordAt("行って", 0);

        Assert.NotNull(achada);
        Assert.Equal(1000020, achada.Main.Id);
        Assert.Contains(achada.Entries, e => e.Id == 1000030);
    }

    [Fact]
    public void Forma_de_dicionario_vem_antes_de_qualquer_conjugacao_do_mesmo_trecho()
    {
        var achada = Lookup.FindWordAt("食べる", 0);

        Assert.NotNull(achada);
        Assert.False(achada.IsInflected);
        Assert.Equal("食べる", achada.Word);
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

    [Fact]
    public void A_consulta_responde_sem_nenhum_acesso_a_rede()
    {
        // O índice é um arquivo local: a consulta não recebe HttpClient nem endereço. Este teste é o contrato do critério "responde sem rede".
        var tipos = typeof(JmdictLookup).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(HttpClient), tipos);
        Assert.Equal("遅刻", new JmdictLookup(SyntheticJmdict.Index()).FindWordAt("遅刻", 1)?.Word);
    }
}
