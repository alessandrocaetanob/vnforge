using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public sealed class SpeakerTextTests
{
    private static readonly ITextEncoder Cp1252 = TextEncoderRegistry.Default.Create("cp1252");
    private static readonly ITextEncoder ShiftJis = TextEncoderRegistry.Default.Create("sjis");

    private static CharacterBook Book(params (string Source, string Target)[] entries) =>
        new(entries.Select(e => new CharacterEntry(e.Source, e.Target)));

    [Fact]
    public void Nome_vem_da_traducao_do_personagem()
    {
        var name = SpeakerText.Resolve("犬井", Book(("犬井", "Inui")), Cp1252);

        Assert.True(name.IsWritable);
        Assert.Equal("Inui", name.Name);
    }

    [Fact]
    public void Traducao_do_personagem_vale_em_qualquer_estrategia()
    {
        Assert.Equal("Inui", SpeakerText.Resolve("犬井", Book(("犬井", "Inui")), ShiftJis).Name);
    }

    [Fact]
    public void Sem_cadastro_a_largura_e_normalizada_quando_a_estrategia_nao_grava_o_original()
    {
        var name = SpeakerText.Resolve("？？？", Book(), Cp1252);

        Assert.True(name.IsWritable);
        Assert.Equal("???", name.Name);
    }

    [Fact]
    public void Sem_cadastro_o_nome_que_a_estrategia_grava_fica_como_esta()
    {
        Assert.Equal("？？？", SpeakerText.Resolve("？？？", Book(), ShiftJis).Name);
        Assert.Equal("犬井", SpeakerText.Resolve("犬井", Book(), ShiftJis).Name);
    }

    [Fact]
    public void Nome_japones_sem_cadastro_na_cp1252_e_erro_com_o_que_fazer()
    {
        var name = SpeakerText.Resolve("犬井", Book(), Cp1252);

        Assert.False(name.IsWritable);
        Assert.Equal(
            "O nome do personagem 犬井 não existe na codificação do jogo (cp1252). Cadastre a tradução do nome no Glossário > Personagens.",
            name.Error);
    }

    [Fact]
    public void Traducao_do_personagem_com_caractere_fora_da_codificacao_e_erro_que_cita_o_caractere()
    {
        var name = SpeakerText.Resolve("犬井", Book(("犬井", "犬 Inui")), Cp1252);

        Assert.False(name.IsWritable);
        Assert.Contains("'犬' (U+72AC)", name.Error, StringComparison.Ordinal);
        Assert.Contains("Glossário > Personagens", name.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Cadastro_com_traducao_vazia_cai_no_nome_original()
    {
        Assert.False(SpeakerText.Resolve("犬井", Book(("犬井", "  ")), Cp1252).IsWritable);
        Assert.Equal("？？？", SpeakerText.Resolve("？？？", Book(("？？？", " ")), ShiftJis).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Sem_falante_nao_ha_nome_nem_erro(string? speaker)
    {
        var name = SpeakerText.Resolve(speaker, Book(), Cp1252);

        Assert.True(name.IsWritable);
        Assert.Equal(string.Empty, name.Name);
    }

    [Theory]
    [InlineData("「", "“")]
    [InlineData("」", "”")]
    [InlineData("『", "‘")]
    [InlineData("』", "’")]
    public void Delimitador_japones_vira_aspa_curva_quando_a_estrategia_nao_o_grava(string delimiter, string expected)
    {
        Assert.Equal(expected, SpeakerText.Delimiter(delimiter, Cp1252));
    }

    [Fact]
    public void Delimitador_que_a_estrategia_grava_fica_e_vazio_continua_vazio()
    {
        Assert.Equal("「", SpeakerText.Delimiter("「", ShiftJis));
        Assert.Equal(string.Empty, SpeakerText.Delimiter(string.Empty, Cp1252));
    }

    [Fact]
    public void Mensagem_cita_o_caractere_e_o_trecho_em_volta()
    {
        const string Text = "Um dia muito longo de verão com 犬 no meio do caminho de casa";
        var index = Text.IndexOf('犬', StringComparison.Ordinal);

        Assert.Equal("o caractere '犬' (U+72AC)", EncodingMessages.Character(Text, index));
        Assert.Equal("…rão com 犬 no meio…", EncodingMessages.Excerpt(Text, index, radius: 8));
        Assert.Equal("Oi 犬", EncodingMessages.Excerpt("Oi 犬", 3));
    }
}
