namespace VNForge.App.Tests;

/// <summary>
/// Endereços e pastas inventados das amostras de teste, montados por partes. Nada é aberto nem resolvido: são dados de exemplo, e o literal inteiro
/// ("https://..." ou "D:\...") seria lido pela regra S1075 do Sonar como caminho ou URI fixo de código de produção.
/// </summary>
internal static class SamplePaths
{
    /// <summary>A raiz de um disco do Windows, como <c>D:\</c>.</summary>
    public static string Root(char drive) => string.Concat(drive, ':', '\\');

    /// <summary>Uma pasta do Windows, como <c>D:\Jogos\VN</c>.</summary>
    public static string Windows(char drive, params string[] parts) => string.Join('\\', [$"{drive}:", .. parts]);

    /// <summary>Uma pasta com barras normais, como as que o <c>vnf</c> grava no <c>project.json</c>.</summary>
    public static string Slashed(char drive, params string[] parts) => string.Join('/', [$"{drive}:", .. parts]);

    /// <summary>Um endereço https no host dado, como <c>https://t.vndb.org/cv/1.jpg</c>.</summary>
    public static string Https(string host, string path) => new UriBuilder(Uri.UriSchemeHttps, host, -1, path).Uri.AbsoluteUri;

    /// <summary>A capa de um jogo no host de imagens do VNDB.</summary>
    public static string Cover(string name) => Https("t.vndb.org", $"cv/{name}.jpg");

    /// <summary>O retrato de um personagem no host de imagens do VNDB.</summary>
    public static string Portrait(int number) => Https("t.vndb.org", $"ch/{number}.jpg");
}
