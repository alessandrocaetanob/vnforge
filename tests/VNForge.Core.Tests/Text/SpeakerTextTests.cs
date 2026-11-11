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
    public void Traducao_do_personagem_com_caractere_fora_da_codificacao_e_erro_que_cita_o_caractere()
    {
        var name = SpeakerText.Resolve("犬井", Book(("犬井", "犬 Inui")), Cp1252);

        Assert.False(name.IsWritable);
        Assert.Contains("'犬' (U+72AC)", name.Error, StringComparison.Ordinal);
        Assert.Contains("Glossário > Personagens", name.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
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
