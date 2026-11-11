using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class ShiftJisTextEncoderTests
{
    private readonly ShiftJisTextEncoder _encoder = new();

    [Fact]
    public void Encode_NaoSubstituiEmSilencio()
    {
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("é"));
        Assert.Throws<EncoderFallbackException>(() => _encoder.Encode("😀"));
    }
}
