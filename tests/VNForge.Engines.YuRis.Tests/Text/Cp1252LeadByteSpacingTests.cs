using VNForge.Core.Text;
using VNForge.Engines.YuRis.Text;

namespace VNForge.Engines.YuRis.Tests.Text;

/// <summary>
/// Na <c>cp1252</c> o exe do jogo ainda lê <c>0xE0</c> a <c>0xFC</c> como lead byte: a letra acentuada se junta ao byte seguinte, e um <c>EF</c> de controle
/// nessa posição se perde (#435). O <see cref="ControlMarkers.ToBytes"/> grava um <c>0x20</c> entre a letra e o <c>EF</c>; texto comum depois do acento, espaço incluído, não muda.
/// </summary>
public class Cp1252LeadByteSpacingTests
{
    private static readonly ITextEncoder Cp1252 = new Cp1252TextEncoder();

    private static byte[] ToBytes(string text) => ControlMarkers.ToBytes(text, Cp1252);

    [Fact]
    public void Acento_antes_do_delimitador_do_falante_da_cp1252_nao_ganha_espaco()
    {
        // « (AB) e » (BB) são texto comum para o motor: a evidência do #62 vale para eles.
        Assert.Equal([.. "Jos"u8, 0xE9, 0xAB], ToBytes("José«"));
        Assert.Equal([.. "oi"u8, 0xE1, 0xBB], ToBytes("oiá»"));
    }
}
