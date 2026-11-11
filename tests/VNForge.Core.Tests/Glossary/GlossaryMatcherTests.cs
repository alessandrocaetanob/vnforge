using VNForge.Core.Glossary;

namespace VNForge.Core.Tests.Glossary;

public class GlossaryMatcherTests
{
    private static readonly GlossaryTerm Senpai = new("先輩", "veterano");

    [Fact]
    public void Ignora_espacos_nas_pontas_do_termo_e_termo_vazio()
    {
        var padded = new GlossaryTerm("　先輩 ", "veterano");
        var blank = new GlossaryTerm("  ", "nada");

        Assert.Equal([padded], GlossaryMatcher.Find("先輩！", [blank, padded]));
    }

    [Theory]
    [InlineData(null)]
    public void Original_nulo_ou_vazio_nao_acha_nada(string? source)
    {
        Assert.Empty(GlossaryMatcher.Find(source, [Senpai]));
    }
}
