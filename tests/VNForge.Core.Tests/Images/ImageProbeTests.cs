using VNForge.Core.Images;

namespace VNForge.Core.Tests.Images;

public sealed class ImageProbeTests
{
    [Fact]
    public void Png_traz_formato_e_tamanho()
    {
        var info = ImageProbe.Read(SyntheticImages.Png(640, 480));

        Assert.Equal(new ImageInfo(ImageFormat.Png, 640, 480), info);
        Assert.Equal("image/png", info!.MediaType);
    }

    [Fact]
    public void Jpeg_traz_formato_e_tamanho()
    {
        var info = ImageProbe.Read(SyntheticImages.Jpeg(1280, 720));

        Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 1280, 720), info);
        Assert.Equal("image/jpeg", info!.MediaType);
    }

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

    [Fact]
    public void Bmp_e_reconhecido_mas_o_modelo_nao_o_aceita()
    {
        var info = ImageProbe.Read(SyntheticImages.Bmp(1024, 768));

        Assert.Equal(new ImageInfo(ImageFormat.Bmp, 1024, 768), info);
        Assert.Null(info!.MediaType);
    }

    [Fact]
    public void Bmp_de_cima_para_baixo_tem_altura_negativa_e_o_tamanho_sai_positivo()
    {
        var bytes = SyntheticImages.Bmp(100, 60);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), -60);

        Assert.Equal(new ImageInfo(ImageFormat.Bmp, 100, 60), ImageProbe.Read(bytes));
    }

    [Fact]
    public void Jpeg_pula_os_segmentos_ate_achar_o_SOF()
    {
        var jpeg = SyntheticImages.Jpeg(64, 48).ToList();
        jpeg.InsertRange(2, [0xFF, 0xDB, 0x00, 0x04, 0x01, 0x02]);

        Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 64, 48), ImageProbe.Read([.. jpeg]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(20)]
    public void Arquivo_cortado_antes_do_tamanho_nao_e_decodificavel(int keep)
    {
        Assert.Null(ImageProbe.Read(SyntheticImages.Png(64, 64).AsSpan(0, keep)));
    }

    [Fact]
    public void Bytes_sem_assinatura_conhecida_nao_sao_imagem()
    {
        Assert.Null(ImageProbe.Read("isto não é uma imagem, só texto longo o bastante"u8));
    }

    [Fact]
    public void Tamanho_zero_nao_e_decodificavel()
    {
        Assert.Null(ImageProbe.Read(SyntheticImages.Png(0, 10)));
    }
}
