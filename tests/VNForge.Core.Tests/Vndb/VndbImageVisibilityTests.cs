using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>As marcas de conteúdo das imagens do VNDB (sexual e violência, médias de 0 a 2) contra a privacidade da Biblioteca.</summary>
public sealed class VndbImageVisibilityTests
{
    private static readonly LibraryPrivacy Hiding = new();
    private static readonly LibraryPrivacy Showing = new(Adult: AdultMode.Show);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.4, 0.4)]
    [InlineData(0.99, 0.99)]
    public void Imagem_sem_marca_aparece_com_a_privacidade_padrao(double sexual, double violence) =>
        Assert.False(VndbImageVisibility.IsHidden(new VndbImageRef("https://t.vndb.org/ch/1.jpg", sexual, violence), Hiding));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 1.8)]
    [InlineData(2, 2)]
    public void Imagem_sexual_ou_violenta_fica_escondida_com_a_privacidade_padrao(double sexual, double violence) =>
        Assert.True(VndbImageVisibility.IsHidden(new VndbImageRef("https://t.vndb.org/ch/1.jpg", sexual, violence), Hiding));

    [Fact]
    public void Mostrar_conteudo_adulto_libera_as_duas_marcas() =>
        Assert.False(VndbImageVisibility.IsHidden(new VndbImageRef("https://t.vndb.org/ch/1.jpg", 2, 2), Showing));

    [Fact]
    public void O_limite_e_o_mesmo_que_a_biblioteca_usa_para_marcar_a_capa_como_adulta()
    {
        var novel = VndbSamples.Game() with { Image = new VndbImage("https://t.vndb.org/cv/1.jpg", null, null, null, VndbImageVisibility.AdultLevel, 0) };

        Assert.True(LibraryMatcher.InfoOf(novel).Adult);
    }

    [Theory]
    [InlineData("https://t.vndb.org/ch/12/3412.jpg", true)]
    [InlineData("https://s.vndb.org/cv/45/1.webp", true)]
    [InlineData("https://vndb.org/x.png", true)]
    [InlineData("http://t.vndb.org/ch/1.jpg", false)]
    [InlineData("https://evil.example/ch/1.jpg", false)]
    [InlineData("https://vndb.org.evil.example/1.jpg", false)]
    [InlineData("https://notvndb.org/1.jpg", false)]
    [InlineData("file:///C:/segredo.png", false)]
    [InlineData("", false)]
    [InlineData("não é endereço", false)]
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
