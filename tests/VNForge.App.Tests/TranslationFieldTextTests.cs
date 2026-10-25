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
    public void Quebra_de_linha_vira_quebra_no_campo_e_o_marcador_do_fim_sai_do_texto()
    {
        var campo = TranslationFieldText.Open("Primeira.\\nSegunda.\\p", Original);

        Assert.Equal("Primeira.\nSegunda.", campo.Text);
        Assert.Equal("\\p", campo.Tail);
        Assert.False(campo.IsRaw);
    }

    [Fact]
    public void Virada_de_pagina_no_meio_vira_linha_em_branco()
    {
        var campo = TranslationFieldText.Open("Uma.\\pOutra.\\p", Original);

        Assert.Equal("Uma.\n\nOutra.", campo.Text);
    }

    [Fact]
    public void Marcadores_que_nao_tem_forma_de_texto_continuam_no_campo()
    {
        var campo = TranslationFieldText.Open("Olá, {nome}.\\p", Original);

        Assert.Equal("Olá, {nome}.", campo.Text);
    }

    [Fact]
    public void Editar_o_campo_grava_quebra_de_linha_e_pagina_e_poe_o_fim_de_volta()
    {
        var campo = TranslationFieldText.Open("Oi.\\p", Original);

        Assert.Equal("Uma.\\nDuas.\\pTrês.\\p", campo.ToStored("Uma.\nDuas.\n\nTrês."));
    }

    [Fact]
    public void Quebras_do_Windows_valem_como_quebras()
    {
        var campo = TranslationFieldText.Open("Oi.\\p", Original);

        Assert.Equal("Uma.\\nDuas.\\pTrês.\\p", campo.ToStored("Uma.\r\nDuas.\r\n\r\nTrês."));
    }

    [Fact]
    public void Sem_traducao_o_fim_vem_do_original()
    {
        var campo = TranslationFieldText.Open(string.Empty, Original);

        Assert.Equal(string.Empty, campo.Text);
        Assert.Equal(string.Empty, campo.ToStored(string.Empty));
        Assert.Equal("Oi.\\p", campo.ToStored("Oi."));
    }

    [Fact]
    public void Sem_traducao_e_original_sem_fim_nada_e_acrescentado()
    {
        var campo = TranslationFieldText.Open(string.Empty, "選択肢");

        Assert.Equal("Oi.", campo.ToStored("Oi."));
    }

    [Fact]
    public void Traducao_gravada_sem_marcador_no_fim_continua_sem()
    {
        var campo = TranslationFieldText.Open("Oi.", Original);

        Assert.Equal("Oi. De novo.", campo.ToStored("Oi. De novo."));
    }

    [Theory]
    [InlineData("Duas\\n\\nquebras\\p")]
    [InlineData("\\p")]
    [InlineData("Marcador solto \\")]
    public void Texto_que_nao_volta_igual_fica_cru_no_campo(string gravado)
    {
        var campo = TranslationFieldText.Open(gravado, Original);

        Assert.True(campo.IsRaw);
        Assert.Equal(gravado, campo.Text);
        Assert.Equal("outro", campo.ToStored("outro"));
    }

    [Fact]
    public void No_modo_cru_a_quebra_digitada_vira_o_marcador_e_nao_um_caractere_de_quebra()
    {
        var campo = TranslationFieldText.Open("Duas\\n\\nquebras\\p", Original);

        Assert.True(campo.IsRaw);
        Assert.Equal("a\\nb\\nc\\nd", campo.ToStored("a\nb\r\nc\rd"));
    }

    [Fact]
    public void Campo_vazio_grava_vazio_mesmo_com_marcador_no_fim()
    {
        var campo = TranslationFieldText.Open("Oi.\\p", Original);

        Assert.Equal(string.Empty, campo.ToStored(string.Empty));
    }
}
