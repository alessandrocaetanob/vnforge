using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class Cp1252TextEncoderTests
{
    private readonly Cp1252TextEncoder _encoder = new();

    [Theory]
    [InlineData("ï", new[] { 0 })]
    [InlineData("日本", new[] { 0, 1 })]
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
    [InlineData("‘Olha’", new[] { 0, 5 })]
    [InlineData("a – b — c", new[] { 2, 6 })]
    public void FindUnencodable_CaractereQueOJogoLeComoInicioDeCaractereDuplo_EhRecusado(string texto, int[] indices)
    {
        Assert.Equal(indices, _encoder.FindUnencodable(texto));
        if (indices.Length > 0)
        {
            Assert.Throws<EncoderFallbackException>(() => _encoder.Encode(texto));
        }
    }

    [Fact]
    public void ComGuillemets_AAberturaEOFechamentoSaoOParDeUmByteDoEstiloAntigo()
    {
        var encoder = Assert.IsType<ISpeakerDelimiterEncoder>(new Cp1252TextEncoder(SpeakerDelimiterStyle.Guillemets), exactMatch: false);

        Assert.Equal(("«", "»"), (encoder.SpeakerOpen, encoder.SpeakerClose));
        Assert.Equal([0xAB], encoder.EncodeSpeakerDelimiter("«"));
        Assert.Equal([0xBB], encoder.EncodeSpeakerDelimiter("»"));
    }

    [Theory]
    [InlineData("x")]
    public void SemSinal_OTextoQueNaoEDelimitadorDoEncoderEhRecusado(string texto)
    {
        Assert.Throws<ArgumentException>(() => _encoder.EncodeSpeakerDelimiter(texto));
    }

    [Fact]
    public void SemSinal_OEspacoNaoSeparavelNaTraducaoEhRecusadoComAExplicacaoDoByte()
    {
        Assert.Equal([1], _encoder.FindUnencodable("a\u00A0b"));
        var ex = Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("a\u00A0b"));
        Assert.Contains("0xA0", ex.Message);
        Assert.Contains("espaço comum", ex.Message);
        Assert.Contains("0xA0", _encoder.ExplainRefusal(0xA0));
    }

    [Fact]
    public void ComGuillemets_OEspacoNaoSeparavelNaTraducaoContinuaGravavel()
    {
        var encoder = new Cp1252TextEncoder(SpeakerDelimiterStyle.Guillemets);

        Assert.Empty(encoder.FindUnencodable("a\u00A0b"));
        Assert.Equal([(byte)'a', 0xA0, (byte)'b'], encoder.Encode("a\u00A0b"));
        Assert.Null(encoder.ExplainRefusal(0xA0));
    }

    [Fact]
    public void EstiloForaDoEnumEhRecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp1252TextEncoder((SpeakerDelimiterStyle)9));
    }
}
