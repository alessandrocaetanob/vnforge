using VNForge.App.Services;

namespace VNForge.App.Tests;

/// <summary>A conversão do campo da tradução: o texto gravado vai para o campo sem marcadores crus e volta byte a byte (regra 3 do CLAUDE.md).</summary>
public sealed class TranslationFieldTextTests
{
    private const string Original = "「ふふっ」\\p";

    [Theory]
    [InlineData("Oi.\\p")]
    [InlineData("Oi.")]
    [InlineData("Primeira linha.\\nSegunda linha.\\p")]
    [InlineData("Primeira página.\\pSegunda página.\\p")]
    [InlineData("A\\nB\\pC\\nD\\p")]
    [InlineData("Espere.\\c\\p")]
    [InlineData("Olá, {nome}.\\p")]
    [InlineData("Duas\\p\\nmisturadas\\p")]
    [InlineData("Já\\n\\nvazia\\p")]
    [InlineData("Barra \\\\ e chave \\{\\p")]
    [InlineData("\\p")]
    [InlineData("  espaços  \\p")]
    [InlineData("Marcador solto \\")]
    [InlineData("Chave {sem fim")]
    public void Ida_e_volta_sem_edicao_devolve_o_mesmo_texto(string gravado)
    {
        var campo = TranslationFieldText.Open(gravado, Original);

        Assert.Equal(gravado, campo.ToStored(campo.Text));
    }

    [Fact]
    public void Editar_o_campo_grava_quebra_de_linha_e_pagina_e_poe_o_fim_de_volta()
    {
        var campo = TranslationFieldText.Open("Oi.\\p", Original);

        Assert.Equal("Uma.\\nDuas.\\pTrês.\\p", campo.ToStored("Uma.\nDuas.\n\nTrês."));
    }

    [Fact]
    public void Campo_vazio_grava_vazio_mesmo_com_marcador_no_fim()
    {
        var campo = TranslationFieldText.Open("Oi.\\p", Original);

        Assert.Equal(string.Empty, campo.ToStored(string.Empty));
    }
}
