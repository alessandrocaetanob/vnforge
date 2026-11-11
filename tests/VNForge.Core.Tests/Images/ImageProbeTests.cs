using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

public sealed class ImageProbeTests
{

    [Fact]
    public void Gif_traz_formato_e_tamanho()
    {
        Assert.Equal(new ImageInfo(ImageFormat.Gif, 300, 200), ImageProbe.Read(SyntheticImages.Gif(300, 200)));
    }

    [Theory]
    [InlineData("estendido")]
    [InlineData("sem perdas")]
    [InlineData("com perdas")]
    public void WebP_traz_formato_e_tamanho_nos_tres_tipos_de_chunk(string kind)
    {
        var bytes = kind switch
        {
            "estendido" => SyntheticImages.WebPExtended(800, 600),
            "sem perdas" => SyntheticImages.WebPLossless(800, 600),
            _ => SyntheticImages.WebPLossy(800, 600),
        };

        var info = ImageProbe.Read(bytes);

        Assert.Equal(new ImageInfo(ImageFormat.WebP, 800, 600), info);
        Assert.Equal("image/webp", info!.MediaType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Arquivo_cortado_antes_do_tamanho_nao_e_decodificavel(int keep)
    {
        Assert.Null(ImageProbe.Read(SyntheticImages.Png(64, 64).AsSpan(0, keep)));
    }

    [Fact]
    public void Tamanho_zero_nao_e_decodificavel()
    {
        Assert.Null(ImageProbe.Read(SyntheticImages.Png(0, 10)));
    }
}
