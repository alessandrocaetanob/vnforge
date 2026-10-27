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
    public void Shift_jis_com_traducao_acentuada_mantem_o_nome_original_como_antes()
    {
        var name = SpeakerText.Resolve("犬井", Book(("犬井", "Mãe")), ShiftJis);

        Assert.True(name.IsWritable);
        Assert.Equal("犬井", name.Name);
    }

    [Fact]
    public void Mensagem_trata_par_substituto_e_borda_do_texto()
    {
        Assert.Equal("o caractere '😀' (U+1F600)", EncodingMessages.Character("a😀", 1));
        Assert.Equal("o caractere '\uD83D' (U+D83D)", EncodingMessages.Character("a\uD83D", 1));
        Assert.Equal("…bcd", EncodingMessages.Excerpt("abcd", 3, radius: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodingMessages.Character("ab", 2));
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
