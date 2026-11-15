using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

[Trait("Risco", "R4")]
public sealed class JapanesePunctuationTests
{
    private static readonly ITextEncoder Cp1252 = TextEncoderRegistry.Default.Create("cp1252");
    private static readonly ITextEncoder Sjis = TextEncoderRegistry.Default.Create("sjis");

    [Theory]
    [InlineData("Hum・・・")]
    public void Reticencias_e_pontos_medios_sao_idempotentes_em_toda_estrategia(string texto)
    {
        foreach (var encoder in new ITextEncoder[] { Cp1252, Sjis, new OnlyAsciiEncoder() })
        {
            var corrigido = JapanesePunctuation.FixUnencodable(texto, encoder);
            Assert.Equal(corrigido, JapanesePunctuation.FixUnencodable(corrigido, encoder));
            var trocado = JapanesePunctuation.ToKeyboard(texto, encoder);
            Assert.Equal(trocado, JapanesePunctuation.ToKeyboard(trocado, encoder));
            Assert.Empty(encoder.FindUnencodable(JapanesePunctuation.FixUnencodable(texto, encoder).Replace("★", string.Empty)));
        }
    }

    [Fact]
    public void Marcador_invalido_deixa_o_texto_inteiro_como_esta()
    {
        Assert.Equal("Oi。\\", JapanesePunctuation.FixUnencodable("Oi。\\", Cp1252));
    }

    [Fact]
    public void Caractere_fora_da_tabela_nao_tem_nome()
    {
        Assert.Null(JapanesePunctuation.NameOf('★'));
        Assert.Null(JapanesePunctuation.KeyboardFor('★'));
    }

    /// <summary>Grava só ASCII: não conhece nem as reticências.</summary>
    private sealed class OnlyAsciiEncoder : ITextEncoder
    {
        public string Id => "ascii";

        public byte[] Encode(string text) => System.Text.Encoding.ASCII.GetBytes(text);

        public IReadOnlyList<int> FindUnencodable(string text) => [.. text.Select((c, i) => (c, i)).Where(x => x.c > 0x7F).Select(x => x.i)];
    }
}
