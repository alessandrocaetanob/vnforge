using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class FoldTextEncoderTests
{
    private readonly FoldTextEncoder _encoder = new();

    private const string Minusculas = "áàâãäéèêëíìîïóòôõöúùûüçñý";
    private const string Maiusculas = "ÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑÝ";

    [Fact]
    public void Id_EhFold()
    {
        Assert.Equal("fold", _encoder.Id);
    }

    [Theory]
    [InlineData("ação", "acao")]
    [InlineData("Coração", "Coracao")]
    [InlineData("ÓÔÕ", "OOO")]
    [InlineData("pingüim", "pinguim")]
    public void Fold_RemoveAcentos(string entrada, string esperado)
    {
        Assert.Equal(esperado, CharacterFolder.Fold(entrada).Text);
    }

    [Fact]
    public void Fold_TodasAsLetrasAcentuadasMinusculas_ViramALetraBase()
    {
        const string esperado = "aaaaaeeeeiiiiooooouuuucny";

        Assert.Equal(esperado, CharacterFolder.Fold(Minusculas).Text);
    }

    [Fact]
    public void Fold_TodasAsLetrasAcentuadasMaiusculas_ViramALetraBase()
    {
        const string esperado = "AAAAAEEEEIIIIOOOOOUUUUCNY";

        Assert.Equal(esperado, CharacterFolder.Fold(Maiusculas).Text);
    }

    [Fact]
    public void Encode_TodasAsLetrasAcentuadas_CodificaEmAscii()
    {
        Assert.Equal(Encoding.ASCII.GetBytes("aaaaaeeeeiiiiooooouuuucny"), _encoder.Encode(Minusculas));
        Assert.Equal(Encoding.ASCII.GetBytes("AAAAAEEEEIIIIOOOOOUUUUCNY"), _encoder.Encode(Maiusculas));
        Assert.Empty(_encoder.FindUnencodable(Minusculas + Maiusculas));
    }

    [Fact]
    public void Fold_LetraDecomposta_RemoveAMarcaCombinante()
    {
        Assert.Equal("acao", CharacterFolder.Fold("ação").Text);
    }

    [Theory]
    [InlineData("«", "\"")]
    [InlineData("»", "\"")]
    [InlineData("ª", "a")]
    [InlineData("º", "o")]
    [InlineData("–", "-")]
    [InlineData("—", "-")]
    [InlineData("¿", "?")]
    [InlineData("¡", "!")]
    [InlineData(" ", " ")]
    public void Fold_TrocaSinaisForaDoShiftJis(string entrada, string esperado)
    {
        Assert.Equal(esperado, CharacterFolder.Fold(entrada).Text);
        Assert.Equal(Encoding.ASCII.GetBytes(esperado), _encoder.Encode(entrada));
    }

    [Fact]
    public void Fold_NaoToca_NoQueOShiftJisJaRepresenta()
    {
        const string texto = "こんにちは「A日」ｱｲｳ ABC 123 ×÷°…“”‘’";

        FoldResult result = CharacterFolder.Fold(texto);

        Assert.Equal(texto, result.Text);
        Assert.Empty(result.Replacements);
        Assert.Equal(GameEncodings.ShiftJis.GetBytes(texto), _encoder.Encode(texto));
    }

    [Fact]
    public void Fold_RelataCadaTrocaComPosicaoOriginalESubstituto()
    {
        FoldResult result = CharacterFolder.Fold("«ação»");

        Assert.Equal("\"acao\"", result.Text);
        Assert.Equal(
            [
                new FoldReplacement(0, "«", "\""),
                new FoldReplacement(2, "ç", "c"),
                new FoldReplacement(3, "ã", "a"),
                new FoldReplacement(5, "»", "\""),
            ],
            result.Replacements);
    }

    [Fact]
    public void Fold_ExpoeSobrescritosELigaturas()
    {
        Assert.Equal("m2 3 1 fi", CharacterFolder.Fold("m² ³ ¹ ﬁ").Text);
    }

    [Fact]
    public void Fold_MarcaNaoLatina_NaoSomeEmSilencio()
    {
        FoldResult result = CharacterFolder.Fold("が");

        Assert.Equal([1], result.Unresolved);
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("が"));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("ゔ"));
    }

    [Fact]
    public void Fold_CaractereSemEquivalente_ContinuaNaoResolvido()
    {
        FoldResult result = CharacterFolder.Fold("a😀bà\ud83d");

        Assert.Equal([1, 5], result.Unresolved);
        Assert.Equal("a😀ba\ud83d", result.Text);
    }

    [Fact]
    public void Encode_DepoisDaTrocaAindaNaoCabe_Lanca()
    {
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("ação 😀"));

        Assert.Contains("Shift-JIS", ex.Message);
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("a\ud83dx"));
    }

    [Fact]
    public void FindUnencodable_ListaIndicesDoTextoOriginal_SemLancar()
    {
        Assert.Equal([4],_encoder.FindUnencodable("ação😀"));
        Assert.Empty(_encoder.FindUnencodable(""));
    }

    [Fact]
    public void FindUnencodable_ConcordaComEncode()
    {
        foreach (string texto in new[] { "ação", "«x»", "日本語ê", "ação 😀", "a\ud83d", "" })
        {
            bool recusa = _encoder.FindUnencodable(texto).Count > 0;
            if (recusa)
            {
                Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));
            }
            else
            {
                _encoder.Encode(texto);
            }
        }
    }

    [Fact]
    public void Fold_SemSobrasForaDoShiftJis_ParaOAlfabetoPortuguesEPontuacaoComum()
    {
        const string texto = Minusculas + Maiusculas + "«»“”‘’–—…ªº¿¡  €©®™½";
        var sjis = new ShiftJisTextEncoder();

        FoldResult result = CharacterFolder.Fold(texto);

        Assert.Empty(result.Unresolved);
        Assert.Empty(sjis.FindUnencodable(result.Text));
    }
}
