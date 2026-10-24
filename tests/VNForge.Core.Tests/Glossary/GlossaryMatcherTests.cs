using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Glossary;

public class GlossaryMatcherTests
{
    private static readonly GlossaryTerm Senpai = new("先輩", "veterano");
    private static readonly GlossaryTerm Club = new("部活", "clube");

    [Fact]
    public void Acha_os_termos_contidos_no_original_na_ordem_do_glossario()
    {
        var found = GlossaryMatcher.Find("「部活の先輩です」", [Senpai, Club]);

        Assert.Equal([Senpai, Club], found);
    }

    [Fact]
    public void Ignora_termos_ausentes_e_devolve_lista_vazia_sem_acerto()
    {
        Assert.Empty(GlossaryMatcher.Find("こんにちは", [Senpai, Club]));
    }

    [Fact]
    public void Ignora_espacos_nas_pontas_do_termo_e_termo_vazio()
    {
        var padded = new GlossaryTerm("　先輩 ", "veterano");
        var blank = new GlossaryTerm("  ", "nada");

        Assert.Equal([padded], GlossaryMatcher.Find("先輩！", [blank, padded]));
    }

    [Fact]
    public void Comparacao_e_ordinal_e_diferencia_maiusculas()
    {
        Assert.Empty(GlossaryMatcher.Find("hello", [new GlossaryTerm("Hello", "olá")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Original_nulo_ou_vazio_nao_acha_nada(string? source)
    {
        Assert.Empty(GlossaryMatcher.Find(source, [Senpai]));
    }

    [Fact]
    public void Glossario_nulo_lanca()
    {
        Assert.Throws<ArgumentNullException>(() => GlossaryMatcher.Find("先輩", null!));
    }
}
