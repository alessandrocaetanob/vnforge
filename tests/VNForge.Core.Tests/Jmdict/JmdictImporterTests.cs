using VNForge.Core.Jmdict;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>O JMdict (XML do EDRDG) vira o índice local (#538). Todo o dicionário aqui é o inventado em <see cref="SyntheticJmdict"/>.</summary>
[Trait("Risco", "R7")]
public sealed class JmdictImporterTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Recusa_um_arquivo_que_nao_e_o_JMdict()
    {
        using var input = new MemoryStream("<html><body>Acesso negado</body></html>"u8.ToArray());

        var erro = Assert.Throws<JmdictFileException>(() => JmdictImporter.Import(input, new MemoryStream()));

        Assert.Contains("JMdict", erro.Message);
    }

    [Fact]
    public void Recusa_um_xml_cortado_no_meio()
    {
        var xml = SyntheticJmdict.XmlBytes();
        using var input = new MemoryStream(xml, 0, xml.Length - 200);

        Assert.Throws<JmdictFileException>(() => JmdictImporter.Import(input, new MemoryStream()));
    }
}
