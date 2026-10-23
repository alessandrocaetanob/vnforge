using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class Cp1252TextEncoderTests
{
    private const string LetrasAcentuadas = "áàâãäéèêëíìîóòôõöúùûüçÁÀÂÃÄÉÈÊËÍÌÎÓÒÔÕÖÚÙÛÜÇñÑ";

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
    [InlineData("Ã", 0xC3)]
    [InlineData("Ç", 0xC7)]
    [InlineData("Õ", 0xD5)]
    public void Encode_LetraAcentuada_UmByteNaPosicaoDoWindows1252(string letra, int esperado)
    {
        Assert.Equal([(byte)esperado], _encoder.Encode(letra));
    }

    [Fact]
    public void Encode_TodasAsLetrasAcentuadasDoPortugues_IgualAConversaoDiretaParaCp1252()
    {
        // O português usa á à â ã é ê í ó ô õ ú ü ç (e as maiúsculas); o resto da lista cobre palavras estrangeiras comuns.
        foreach (char letra in LetrasAcentuadas)
        {
            string texto = letra.ToString();
            Assert.Equal(GameEncodings.Windows1252.GetBytes(texto), _encoder.Encode(texto));
            Assert.Empty(_encoder.FindUnencodable(texto));
        }
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

    [Fact]
    public void FindUnencodable_TremaNoI_EhNaoCodificavel()
    {
        Assert.Equal([0], _encoder.FindUnencodable("ï"));
        Assert.Equal([2, 6], _encoder.FindUnencodable("abïcdeï"));
    }

    [Fact]
    public void FindUnencodable_TremaNoI_NaoApontaOutrasLetrasAcentuadas()
    {
        Assert.Equal([4], _encoder.FindUnencodable("açãoïé"));
    }

    [Fact]
    public void Encode_TremaNoI_LancaComMensagemEmPortugues()
    {
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("naïve"));

        Assert.Contains("0xEF", ex.Message);
        Assert.Contains("posição 2", ex.Message);
    }

    [Fact]
    public void FindUnencodable_ConcordaComEncode()
    {
        foreach (string texto in new[] { "ï", "日本語", "😀", "a\ud83dx", "日naïve" })
        {
            Assert.NotEmpty(_encoder.FindUnencodable(texto));
            Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));
        }

        foreach (string texto in new[] { "ação", "€", "" })
        {
            Assert.Empty(_encoder.FindUnencodable(texto));
            Assert.Equal(GameEncodings.Windows1252.GetBytes(texto), _encoder.Encode(texto));
        }
    }

    [Fact]
    public void Encode_MensagemCitaOIMesmoQuandoNaoEOPrimeiroRecusado()
    {
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("日naïve"));

        Assert.Contains("0xEF", ex.Message);
    }

    [Fact]
    public void Encode_CaractereForaDoWindows1252_NaoSubstituiEmSilencio()
    {
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("日本語"));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("😀"));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("a\ud83dx"));
    }

    [Fact]
    public void FindUnencodable_ParSubstitutoContaUmaVezNoPrimeiroChar()
    {
        Assert.Equal([1, 5], _encoder.FindUnencodable("a😀bcï"));
    }

    [Fact]
    public void FindUnencodable_TextoLimpo_Vazio()
    {
        Assert.Empty(_encoder.FindUnencodable("Ação à vista"));
    }
}
