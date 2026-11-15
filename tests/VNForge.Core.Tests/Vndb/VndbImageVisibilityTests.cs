using VNForge.Core.Library;
using VNForge.Core.Vndb;

namespace VNForge.Core.Tests.Vndb;

/// <summary>As marcas de conteúdo das imagens do VNDB (sexual e violência, médias de 0 a 2) contra a privacidade da Biblioteca.</summary>
[Trait("Risco", "R9")]
public sealed class VndbImageVisibilityTests
{
    [Theory]
    [InlineData("https://vndb.org/x.png", true)]
    public void So_baixa_de_vndb_org_por_https(string url, bool allowed) =>
        Assert.Equal(allowed, VndbImageVisibility.IsAllowedSource(url));
}
