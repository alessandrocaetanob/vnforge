namespace VNForge.App.Tests;

public sealed class SamplePathsTests
{
    [Fact]
    public void Raiz_do_disco_e_a_letra_os_dois_pontos_e_uma_barra_invertida()
    {
        Assert.Equal(["E:", string.Empty], SamplePaths.Root('E').Split('\\'));
    }

    [Fact]
    public void Pasta_do_Windows_junta_as_partes_com_barra_invertida()
    {
        Assert.Equal(["D:", "Jogos", "VN"], SamplePaths.Windows('D', "Jogos", "VN").Split('\\'));
        Assert.Equal(["E:", "VN"], SamplePaths.Windows('E', "VN").Split('\\'));
    }

    [Fact]
    public void Pasta_com_barras_normais_junta_as_partes_com_barra()
    {
        Assert.Equal(["D:", "Jogos", "Qualquer"], SamplePaths.Slashed('D', "Jogos", "Qualquer").Split('/'));
    }

    [Fact]
    public void Capa_e_retrato_ficam_no_host_de_imagens_do_VNDB_com_https_e_sem_porta()
    {
        var cover = new Uri(SamplePaths.Cover("korin_no_mori"));
        var portrait = new Uri(SamplePaths.Portrait(101));

        Assert.Equal(Uri.UriSchemeHttps, cover.Scheme);
        Assert.Equal("t.vndb.org", cover.Host);
        Assert.True(cover.IsDefaultPort);
        Assert.Equal("/cv/korin_no_mori.jpg", cover.AbsolutePath);
        Assert.Equal("t.vndb.org", portrait.Host);
        Assert.Equal("/ch/101.jpg", portrait.AbsolutePath);
    }

    [Fact]
    public void O_endereco_montado_e_a_string_exata_do_uri_sem_barra_a_mais()
    {
        var text = SamplePaths.Cover("a");

        Assert.StartsWith(Uri.UriSchemeHttps + Uri.SchemeDelimiter + "t.vndb.org/", text, StringComparison.Ordinal);
        Assert.EndsWith("/cv/a.jpg", text, StringComparison.Ordinal);
        Assert.DoesNotContain("//cv", text, StringComparison.Ordinal);
    }
}
