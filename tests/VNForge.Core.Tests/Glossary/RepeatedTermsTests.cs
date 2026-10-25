using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Glossary;

public class RepeatedTermsTests
{
    private static IEnumerable<string> Repeat(string text, int times) => Enumerable.Repeat(text, times);

    [Fact]
    public void Respeita_a_frequencia_minima_de_cinco_linhas()
    {
        string[] sources = [.. Repeat("魔法の杖を振る", 5), .. Repeat("勇者が来た", 4)];

        var found = RepeatedTerms.Count(sources);

        Assert.Equal(new TermCandidate("魔法", 5), found.Single(c => c.Source == "魔法"));
        Assert.DoesNotContain(found, c => c.Source == "勇者");
    }

    [Fact]
    public void Conta_linhas_e_nao_ocorrencias_dentro_da_linha()
    {
        var found = RepeatedTerms.Count(["魔法魔法魔法の杖"], minFrequency: 1);

        Assert.Equal(1, found.Single(c => c.Source == "魔法魔法魔法").Lines);
    }

    [Theory]
    [InlineData("魔", false)]
    [InlineData("魔法", true)]
    [InlineData("魔法学校長先", true)]
    [InlineData("魔法学校長先生", false)]
    public void So_entram_sequencias_de_2_a_6_caracteres(string run, bool expected)
    {
        var found = RepeatedTerms.Count(Repeat($"、{run}、", 5));

        Assert.Equal(expected, found.Any(c => c.Source == run));
    }

    [Fact]
    public void Sequencia_maior_que_seis_nao_gera_pedacos()
    {
        var found = RepeatedTerms.Count(Repeat("魔法学校長先生", 5));

        Assert.Empty(found);
    }

    [Fact]
    public void Hiragana_e_pontuacao_separam_as_sequencias()
    {
        var found = RepeatedTerms.Count(Repeat("ラムネを飲んだ。ラムネ。", 5));

        Assert.Equal("ラムネ", Assert.Single(found).Source);
    }

    [Fact]
    public void Kanji_e_katakana_juntos_formam_uma_sequencia()
    {
        var found = RepeatedTerms.Count(Repeat("魔法ランプ", 5));

        Assert.Equal("魔法ランプ", Assert.Single(found).Source);
    }

    [Fact]
    public void A_barra_de_prolongamento_fica_na_sequencia_de_katakana()
    {
        var found = RepeatedTerms.Count(Repeat("コーヒーだ", 5));

        Assert.Equal("コーヒー", Assert.Single(found).Source);
    }

    [Fact]
    public void Ordena_pela_frequencia_e_desempata_pelo_texto()
    {
        string[] sources = [.. Repeat("刀剣", 6), .. Repeat("弓矢", 8), .. Repeat("盾", 9), .. Repeat("斧槍", 6)];

        var found = RepeatedTerms.Count(sources);

        Assert.Equal(["弓矢", "刀剣", "斧槍"], found.Select(c => c.Source));
    }

    [Fact]
    public void Exclui_o_que_ja_esta_no_glossario()
    {
        var found = RepeatedTerms.Find(Repeat("魔法学院の先輩と剣士", 5), known: ["魔法学院", "剣士"]);

        Assert.Equal(["先輩"], found.Select(c => c.Source));
    }

    [Fact]
    public void Exclui_tambem_a_sequencia_que_e_parte_de_um_termo_conhecido()
    {
        var found = RepeatedTerms.Find(Repeat("魔法学院", 5), known: ["魔法学院生"]);

        Assert.Empty(found);
    }

    [Fact]
    public void Termo_conhecido_vazio_ou_com_espacos_nao_exclui_nada()
    {
        var found = RepeatedTerms.Find(Repeat("魔法", 5), known: ["", "  "]);

        Assert.Single(found);
    }

    [Fact]
    public void Limita_o_numero_de_candidatos()
    {
        string[] sources = [.. Repeat("魔法", 9), .. Repeat("勇者", 8), .. Repeat("先輩", 7)];

        var found = RepeatedTerms.Find(sources, known: [], maxResults: 2);

        Assert.Equal(["魔法", "勇者"], found.Select(c => c.Source));
    }

    [Fact]
    public void Texto_sem_kanji_nem_katakana_nao_gera_candidatos()
    {
        Assert.Empty(RepeatedTerms.Count(Repeat("hello world, こんにちは", 10)));
    }
}
