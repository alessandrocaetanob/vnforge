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
    public void Traduz_as_entidades_do_DTD_para_os_codigos_do_JMdict()
    {
        var entry = Assert.Single(SyntheticJmdict.Index().Find("行く"));

        Assert.Equal(["v5k-s", "vi"], entry.Senses[0].PartsOfSpeech);
        Assert.Equal(["v5k-s", "vi"], entry.PartsOfSpeech);
    }

    [Fact]
    public void Sentido_sem_classe_herda_a_do_sentido_anterior()
    {
        var entry = Assert.Single(SyntheticJmdict.Index().Find("遅刻"));

        Assert.Equal(["n", "vs"], entry.Senses[1].PartsOfSpeech);
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
    public void Entrada_so_com_kana_tem_a_leitura_como_unica_forma()
    {
        var entry = Assert.Single(SyntheticJmdict.Index().Find("だ"));

        Assert.Empty(entry.Kanji);
        Assert.Equal(["だ"], entry.Readings);
    }

    [Theory]
    [InlineData("行く", 10)]
    [InlineData("遅刻", 12)]
    [InlineData("揃う", 30)]
    [InlineData("五分", JmdictEntry.NoRank)]
    public void A_frequencia_vira_o_menor_valor_entre_as_marcas(string palavra, int esperado)
    {
        Assert.Equal(esperado, Assert.Single(SyntheticJmdict.Index().Find(palavra)).Rank);
    }

    [Fact]
    public void Guarda_a_data_de_geracao_do_arquivo_e_a_contagem()
    {
        var index = SyntheticJmdict.Index();

        Assert.Equal("2026-11-09", index.Created);
        Assert.Equal(13, index.EntryCount);
    }

    [Fact]
    public void Importa_o_arquivo_gzip_para_um_arquivo_de_indice_que_abre_de_novo()
    {
        var gz = Path.Combine(temp.Path, "JMdict_e.gz");
        var saida = Path.Combine(temp.Path, "jmdict.vnfdict");
        File.WriteAllBytes(gz, SyntheticJmdict.GzipBytes());

        JmdictImporter.ImportGzipFile(gz, saida);
        var index = JmdictIndex.Open(saida);

        Assert.Equal(13, index.EntryCount);
        Assert.Equal("食べる", Assert.Single(index.Find("たべる")).Kanji[0]);
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

    [Fact]
    public void Importar_o_mesmo_arquivo_duas_vezes_gera_o_mesmo_indice()
    {
        using var a = new MemoryStream();
        using var b = new MemoryStream();

        JmdictImporter.Import(new MemoryStream(SyntheticJmdict.XmlBytes()), a);
        JmdictImporter.Import(new MemoryStream(SyntheticJmdict.XmlBytes()), b);

        Assert.Equal(a.ToArray(), b.ToArray());
    }
}
