using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>As marcas de conteúdo das imagens do VNDB (sexual e violência, médias de 0 a 2) contra a privacidade da Biblioteca.</summary>
public sealed class VndbImageVisibilityTests
{

    [Theory]
    [InlineData("https://vndb.org/x.png", true)]
    public void So_baixa_de_vndb_org_por_https(string url, bool allowed) =>
        Assert.Equal(allowed, VndbImageVisibility.IsAllowedSource(url));

    [Fact]
    public void A_referencia_de_capa_usa_a_miniatura_e_a_de_retrato_usa_a_imagem()
    {
        var image = new VndbImage("https://t.vndb.org/cv/1.jpg", "https://t.vndb.org/cv.t/1.jpg", 800, 1200, 0.5, 1.2);

        Assert.Equal(new VndbImageRef("https://t.vndb.org/cv.t/1.jpg", 0.5, 1.2), VndbImageRef.Cover(image));
        Assert.Equal(new VndbImageRef("https://t.vndb.org/cv/1.jpg", 0.5, 1.2), VndbImageRef.Portrait(image));
        Assert.Equal("https://t.vndb.org/cv/1.jpg", VndbImageRef.Cover(image with { ThumbnailUrl = null })!.Url);
        Assert.Null(VndbImageRef.Cover(null));
        Assert.Null(VndbImageRef.Portrait(null));
    }
}
