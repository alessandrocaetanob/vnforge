using VNForge.Core.Jmdict;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Jmdict;

/// <summary>O JMdict (XML do EDRDG) vira o índice local (#538). Todo o dicionário aqui é o inventado em <see cref="SyntheticJmdict"/>.</summary>
public sealed class JmdictImporterTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Importa_leituras_sentidos_e_classes_da_entrada()
    {
        var index = SyntheticJmdict.Index();

        var entry = Assert.Single(index.Find("遅刻"));

        Assert.Equal(1000010, entry.Id);
        Assert.Equal(["遅刻"], entry.Kanji);
        Assert.Equal(["ちこく"], entry.Readings);
        Assert.Equal(2, entry.Senses.Count);
        Assert.Equal(["lateness", "tardiness"], entry.Senses[0].Glosses);
        Assert.Equal("lateness; tardiness", entry.Senses[0].GlossText);
    }

    [Fact]
    public void Acha_a_entrada_pela_leitura_em_kana_e_pela_forma_alternativa()
    {
        var index = SyntheticJmdict.Index();

        Assert.Equal(1000010, Assert.Single(index.Find("ちこく")).Id);
        Assert.Equal(1000020, Assert.Single(index.Find("ゆく")).Id);
        Assert.Equal(1000020, Assert.Single(index.Find("往く")).Id);
        Assert.Empty(index.Find("遅"));
        Assert.Empty(index.Find(string.Empty));
    }

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

    [Fact]
    public void Abrir_um_indice_que_nao_e_do_VNForge_ou_esta_truncado_e_erro_com_o_caminho()
    {
        var arquivo = Path.Combine(temp.Path, "jmdict.vnfdict");
        File.WriteAllText(arquivo, "isto não é um índice");

        var erro = Assert.Throws<JmdictFileException>(() => JmdictIndex.Open(arquivo));
        Assert.Contains(arquivo, erro.Message);

        using var input = new MemoryStream(SyntheticJmdict.XmlBytes());
        using var saida = new MemoryStream();
        JmdictImporter.Import(input, saida);
        File.WriteAllBytes(arquivo, saida.ToArray().AsSpan(0, 60).ToArray());
        Assert.Throws<JmdictFileException>(() => JmdictIndex.Open(arquivo));
    }
}
