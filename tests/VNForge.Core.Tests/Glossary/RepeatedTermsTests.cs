using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Glossary;

public class RepeatedTermsTests
{
    private static IEnumerable<string> Repeat(string text, int times) => Enumerable.Repeat(text, times);

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
    public void Limita_o_numero_de_candidatos()
    {
        string[] sources = [.. Repeat("魔法", 9), .. Repeat("勇者", 8), .. Repeat("先輩", 7)];

        var found = RepeatedTerms.Find(sources, known: [], maxResults: 2);

        Assert.Equal(["魔法", "勇者"], found.Select(c => c.Source));
    }
}
