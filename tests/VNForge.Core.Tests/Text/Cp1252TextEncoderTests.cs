using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class Cp1252TextEncoderTests
{
    private readonly Cp1252TextEncoder _encoder = new();

    [Fact]
    public void Id_EhCp1252()
    {
        Assert.Equal("cp1252", _encoder.Id);
    }

    [Theory]
    [InlineData("á", 0xE1)]
    [InlineData("à", 0xE0)]
    [InlineData("â", 0xE2)]
    [InlineData("ã", 0xE3)]
    [InlineData("é", 0xE9)]
    [InlineData("ê", 0xEA)]
    [InlineData("í", 0xED)]
    [InlineData("ó", 0xF3)]
    [InlineData("ô", 0xF4)]
    [InlineData("õ", 0xF5)]
    [InlineData("ú", 0xFA)]
    [InlineData("ü", 0xFC)]
    [InlineData("ç", 0xE7)]
    [InlineData("Á", 0xC1)]
    [InlineData("À", 0xC0)]
    [InlineData("Â", 0xC2)]
    [InlineData("Ã", 0xC3)]
    [InlineData("É", 0xC9)]
    [InlineData("Ê", 0xCA)]
    [InlineData("Í", 0xCD)]
    [InlineData("Ó", 0xD3)]
    [InlineData("Ô", 0xD4)]
    [InlineData("Õ", 0xD5)]
    [InlineData("Ú", 0xDA)]
    [InlineData("Ü", 0xDC)]
    [InlineData("Ç", 0xC7)]
    public void Encode_LetraAcentuada_UmByteNaPosicaoDoWindows1252(string letra, int esperado)
    {
        Assert.Equal([(byte)esperado], _encoder.Encode(letra));
        Assert.Empty(_encoder.FindUnencodable(letra));
    }

    [Fact]
    public void Encode_FraseComAcentos_UmBytePorCaractere()
    {
        const string frase = "Não é ação, é coração — só você, às vezes.";

        byte[] bytes = _encoder.Encode(frase);

        Assert.Equal(frase.Length, bytes.Length);
        Assert.Equal(GameEncodings.Windows1252.GetBytes(frase), bytes);
    }

    [Fact]
    public void Encode_SemBom_SemTerminador()
    {
        Assert.Equal(Encoding.ASCII.GetBytes("abc"), _encoder.Encode("abc"));
        Assert.Empty(_encoder.Encode(""));
    }

    [Theory]
    [InlineData("ï", new[] { 0 })]
    [InlineData("abïcdeï", new[] { 2, 6 })]
    [InlineData("açãoïé", new[] { 4 })]
    [InlineData("a😀bcï", new[] { 1, 5 })]
    [InlineData("日本", new[] { 0, 1 })]
    [InlineData("Ação à vista", new int[0])]
    public void FindUnencodable_ApontaOsIndicesQueEncodeRecusa(string texto, int[] indices)
    {
        Assert.Equal(indices, _encoder.FindUnencodable(texto));

        if (indices.Length > 0)
        {
            Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));
        }
        else
        {
            Assert.Equal(GameEncodings.Windows1252.GetBytes(texto), _encoder.Encode(texto));
        }
    }

    [Theory]
    [InlineData("naïve", "posição 2")]
    [InlineData("日naïve", "0xEF")]
    public void Encode_TremaNoI_LancaComMensagemEmPortugues(string texto, string trecho)
    {
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));

        Assert.Contains(trecho, ex.Message);
        Assert.Contains("0xEF", ex.Message);
    }
}
