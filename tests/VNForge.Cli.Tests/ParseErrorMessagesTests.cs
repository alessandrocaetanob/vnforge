namespace VNForge.Cli.Tests;

public sealed class ParseErrorMessagesTests
{
    [Theory]
    [InlineData("Option '--game' is required.", "A opção '--game' é obrigatória.")]
    [InlineData(
        "Cannot parse argument 'abc' for option '--limit' as expected type 'System.Int32'.",
        "O valor 'abc' não é válido para a opção '--limit' (esperado: número inteiro).")]
    [InlineData(
        "Cannot parse argument 'abc' as expected type 'System.Int32'.",
        "O valor 'abc' não é válido (esperado: número inteiro).")]
    [InlineData("Unrecognized command or argument '--bogus'.", "'--bogus' não é um comando, opção ou argumento conhecido.")]
    [InlineData("Required argument missing for command: 'sub'.", "Falta um argumento obrigatório do comando 'sub'.")]
    [InlineData("Required argument missing for option: '--game'.", "A opção '--game' precisa de um valor.")]
    [InlineData("Required command was not provided.", "Informe um comando.")]
    [InlineData("Comando ou argumento '--bogus' não reconhecido.", "'--bogus' não é um comando, opção ou argumento conhecido.")]
    [InlineData("Argumento obrigatório ausente para o comando: 'sub'.", "Falta um argumento obrigatório do comando 'sub'.")]
    [InlineData("Argumento obrigatório ausente para a opção: '--game'.", "A opção '--game' precisa de um valor.")]
    [InlineData("O comando necessário não foi fornecido.", "Informe um comando.")]
    public void Mensagens_conhecidas_viram_portugues(string original, string esperado)
    {
        Assert.Equal(esperado, ParseErrorMessages.Translate(original));
    }

    [Fact]
    public void Mensagem_desconhecida_mantem_o_texto_original_depois_de_um_prefixo()
    {
        Assert.Equal(
            "Uso inválido: File does not exist: 'x'.",
            ParseErrorMessages.Translate("File does not exist: 'x'."));
    }

    [Fact]
    public void Tipo_desconhecido_aparece_pelo_nome_curto()
    {
        Assert.Equal(
            "O valor 'x' não é válido para a opção '--data' (esperado: DateTime).",
            ParseErrorMessages.Translate("Cannot parse argument 'x' for option '--data' as expected type 'System.DateTime'."));
    }
}
