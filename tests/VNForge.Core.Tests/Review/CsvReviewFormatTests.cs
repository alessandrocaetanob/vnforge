using System.Text;
using VNForge.Core.Review;

namespace VNForge.Core.Tests.Review;

public class CsvReviewFormatTests
{
    private static ReviewRow Linha(string id, string translation = "", string notes = "", string source = "原文", string speaker = "") =>
        new(0, id, "dialogue", speaker, source, translation, "translated", notes);

    private static IReadOnlyList<ReviewRow> IdaEVolta(params ReviewRow[] linhas) =>
        ReviewFormats.Csv.Read(ReviewFormats.Csv.Write(linhas), "x.csv");

    [Fact]
    public void Grava_utf8_com_bom_cabecalho_fixo_e_crlf()
    {
        var bytes = ReviewFormats.Csv.Write([Linha("a:1", "olá")]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(
            "id,kind,speaker,source,translation,status,notes\r\na:1,dialogue,,原文,olá,translated,\r\n",
            Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void Ida_e_volta_preserva_virgula_aspas_quebra_de_linha_e_marcadores()
    {
        var original = Linha("a:1", "ele disse \"oi\", e\nfoi\\p", notes: "linha1\r\nlinha2");

        var lida = Assert.Single(IdaEVolta(original));

        Assert.Equal(original.Translation, lida.Translation);
        Assert.Equal(original.Notes, lida.Notes);
        Assert.Equal("a:1", lida.Id);
    }

    [Fact]
    public void Le_arquivo_sem_bom_e_com_fim_de_linha_lf_e_sem_quebra_final()
    {
        var csv = "id,kind,speaker,source,translation,status,notes\na:1,dialogue,,x,t1,translated,\na:2,dialogue,,y,t2,machine,n";

        var linhas = ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv");

        Assert.Equal(["a:1", "a:2"], linhas.Select(l => l.Id));
        Assert.Equal("n", linhas[1].Notes);
        Assert.Equal([2, 3], linhas.Select(l => l.Line));
    }

    [Fact]
    public void Aceita_colunas_em_outra_ordem_e_colunas_extras()
    {
        var csv = "extra,notes,status,translation,id\r\nz,n,reviewed,t,a:1\r\n";

        var linha = Assert.Single(ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));

        Assert.Equal(("a:1", "t", "reviewed", "n"), (linha.Id, linha.Translation, linha.Status, linha.Notes));
    }

    [Theory]
    [InlineData("id,translation,status\r\na:1,t,translated\r\n", "notes")]
    [InlineData("kind,translation,status,notes\r\n", "id")]
    public void Cabecalho_sem_coluna_obrigatoria_e_erro(string csv, string coluna)
    {
        var ex = Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));

        Assert.Contains($"'{coluna}'", ex.Message);
    }

    [Fact]
    public void Linha_com_numero_errado_de_colunas_e_erro_com_numero_da_linha()
    {
        var csv = "id,translation,status,notes\r\na:1,t,translated,\r\na:2,t\r\n";

        var ex = Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));

        Assert.Contains("linha 3", ex.Message);
    }

    [Fact]
    public void Aspas_sem_fechar_e_erro()
    {
        var csv = "id,translation,status,notes\r\na:1,\"t,translated,\r\n";

        Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));
    }

    [Fact]
    public void Arquivo_fora_de_utf8_e_erro()
    {
        Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read([0x69, 0x64, 0x2C, 0xFF, 0xFE], "x.csv"));
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("-Olá")]
    [InlineData("@x")]
    [InlineData("+1")]
    [InlineData("'=x")]
    [InlineData("''-x")]
    [InlineData("'normal")]
    public void Texto_que_o_excel_leria_como_formula_ganha_apostrofo_e_volta_igual(string texto)
    {
        var bytes = ReviewFormats.Csv.Write([Linha("a:1", texto)]);
        var gravado = Encoding.UTF8.GetString(bytes);

        if (texto is not "'normal")
        {
            Assert.Contains(",'" + texto + ",", gravado);
        }

        Assert.Equal(texto, ReviewFormats.Csv.Read(bytes, "x.csv").Single().Translation);
    }

    [Fact]
    public void Aceita_ponto_e_virgula_do_excel_pt_br()
    {
        var csv = "id;translation;status;notes\r\na:1;\"um, dois\";translated;\r\n";

        var linha = Assert.Single(ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));

        Assert.Equal("um, dois", linha.Translation);
    }

    [Theory]
    [InlineData("id,translation,status,notes\r\na:1,ab\"c,translated,\r\n")]
    [InlineData("id,translation,status,notes\r\na:1,\"ab\"c,translated,\r\n")]
    public void Aspas_fora_do_lugar_sao_erro(string csv)
    {
        Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));
    }

    [Fact]
    public void Aceita_cr_isolado_como_fim_de_linha()
    {
        var csv = "id,translation,status,notes\ra:1,t,translated,\r";

        Assert.Single(ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));
    }

    [Fact]
    public void Linhas_em_branco_sao_ignoradas()
    {
        var csv = "id,translation,status,notes\r\n\r\na:1,t,translated,\r\n\r\n";

        Assert.Single(ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));
    }

    [Fact]
    public void Registro_acha_o_formato_pelo_nome_e_pela_extensao()
    {
        Assert.Same(ReviewFormats.Csv, ReviewFormats.Find("CSV"));
        Assert.Same(ReviewFormats.Csv, ReviewFormats.FromPath("rev.CSV"));
        Assert.Null(ReviewFormats.Find("po"));
        Assert.Null(ReviewFormats.FromPath("rev.po"));
    }
}
