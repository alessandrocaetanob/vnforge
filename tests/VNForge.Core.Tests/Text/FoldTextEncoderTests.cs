using System.Text;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Text;

public class FoldTextEncoderTests
{
    private readonly FoldTextEncoder _encoder = new();

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
}
