using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class ShiftJisTextEncoderTests
{
    private readonly ShiftJisTextEncoder _encoder = new();

    [Fact]
    public void Id_EhSjis()
    {
        Assert.Equal("sjis", _encoder.Id);
    }

    [Theory]
    [InlineData("こんにちは")]
    [InlineData("日本語")]
    [InlineData("「A日」")]
    [InlineData("ｱｲｳ")]
    [InlineData("")]
    public void Encode_TextoJapones_IgualAConversaoDiretaParaCp932(string texto)
    {
        Assert.Equal(GameEncodings.ShiftJis.GetBytes(texto), _encoder.Encode(texto));
    }

    [Fact]
    public void Encode_CaractereNaoRepresentavel_LancaComMensagemEmPortugues()
    {
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("ação"));

        Assert.Contains("Shift-JIS", ex.Message);
    }

    [Fact]
    public void Encode_NaoSubstituiEmSilencio()
    {
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("é"));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("😀"));
    }

    [Fact]
    public void Encode_SurrogateSolitario_Lanca()
    {
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("a\ud83dx"));
    }

    [Fact]
    public void FindUnencodable_ListaIndicesSemLancar()
    {
        Assert.Equal([1, 3], _encoder.FindUnencodable("aãbç"));
        Assert.Empty(_encoder.FindUnencodable("日本語 abc"));
        Assert.Empty(_encoder.FindUnencodable(""));
    }

    [Fact]
    public void FindUnencodable_ParSubstitutoContaUmaVezNoPrimeiroChar()
    {
        Assert.Equal([1, 5], _encoder.FindUnencodable("a😀bcé"));
    }

    [Fact]
    public void FindUnencodable_SurrogateSolitario_EhNaoRepresentavel()
    {
        Assert.Equal([1, 3], _encoder.FindUnencodable("a\ud83dx\ude00"));
    }

    [Fact]
    public void FindUnencodable_ConcordaComEncode()
    {
        const string texto = "日本語ação";

        Assert.NotEmpty(_encoder.FindUnencodable(texto));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));
    }

    [Fact]
    public void Encode_SemBom_SemTerminador()
    {
        Assert.Equal(Encoding.ASCII.GetBytes("abc"), _encoder.Encode("abc"));
    }
}
