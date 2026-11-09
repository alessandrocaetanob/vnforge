namespace VNForge.Core.Jmdict;

/// <summary>Onde o JMdict mora na internet: o arquivo que o app baixa e a declaração de licença do EDRDG. É o único lugar do código que escreve esses endereços.</summary>
public static class JmdictSource
{
    /// <summary>O JMdict só com as glosas em inglês (<c>JMdict_e.gz</c>, cerca de 10 MB).</summary>
    public const string DownloadUrl = "https://www.edrdg.org/pub/Nihongo/JMdict_e.gz";

    /// <summary>A declaração de licença do EDRDG, que a atribuição do cartão e do aviso de download cita.</summary>
    public const string LicenseUrl = "https://www.edrdg.org/edrdg/licence.html";
}
