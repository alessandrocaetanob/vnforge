namespace VNForge.Cli.Tests;

[Trait("Risco", "R7")]
public sealed class ParseErrorMessagesTests
{
    [Theory]
    [InlineData("Option '--game' is required.", "A opção '--game' é obrigatória.")]
    public void Mensagens_conhecidas_viram_portugues(string original, string esperado)
    {
        Assert.Equal(esperado, ParseErrorMessages.Translate(original));
    }

    [Fact]
    public void Mensagem_desconhecida_sai_generica_em_portugues_sem_o_texto_original()
    {
        var translated = ParseErrorMessages.Translate("File does not exist: 'x'.");

        Assert.Equal("Uso inválido.", translated);
        Assert.DoesNotContain("File", translated);
    }
}
