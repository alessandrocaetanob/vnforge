using VNForge.Core.Review;

namespace VNForge.Core.Tests.Review;

public class XlsxReviewFormatTests
{
    private static readonly IReviewFormat Xlsx = ReviewFormats.Xlsx;

    private static ReviewRow Linha(string id, string source = "おはよう", string translation = "Bom dia", string notes = "") =>
        new(0, id, "dialogue", "美咲", source, translation, "machine", notes);

    [Fact]
    public void Esta_registrado_pelo_nome_e_pela_extensao()
    {
        Assert.Same(Xlsx, ReviewFormats.Find("XLSX"));
        Assert.Same(Xlsx, ReviewFormats.FromPath("C:/x/revisao.XLSX"));
        Assert.Equal(".xlsx", Xlsx.Extension);
    }

    [Fact]
    public void Ida_e_volta_preserva_japones_quebras_de_linha_espacos_e_texto_de_formula()
    {
        var rows = new[]
        {
            Linha("a:1"),
            Linha("a:2", source: "「はい」\n次の行", translation: "  espaço nas pontas  ", notes: "linha 1\r\nlinha 2"),
            Linha("a:3", translation: "=SOMA(A1)", notes: "+1 e <&> \"aspas\""),
            Linha("a:4", translation: "", notes: ""),
            Linha("a:5", translation: "'apóstrofo", notes: "@nota"),
        };

        var lidas = Xlsx.Read(Xlsx.Write(rows), "r.xlsx");

        Assert.Equal(rows.Length, lidas.Count);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Equal(rows[i] with { Line = i + 2 }, lidas[i]);
        }
    }

    [Fact]
    public void Aceita_colunas_em_outra_ordem_extras_e_celulas_vazias_omitidas()
    {
        var original = Xlsx.Write([Linha("a:1", translation: "", notes: "")]);
        var lidas = Xlsx.Read(original, "r.xlsx");

        Assert.Equal("", lidas[0].Translation);
        Assert.Equal("", lidas[0].Notes);
        Assert.Equal("a:1", lidas[0].Id);
    }

    [Fact]
    public void Arquivo_que_nao_e_xlsx_e_recusado_com_mensagem()
    {
        var erro = Assert.Throws<ReviewImportException>(() => Xlsx.Read("id,x"u8.ToArray(), "r.xlsx"));
        Assert.Contains("r.xlsx", erro.Message);
        Assert.Contains("XLSX", erro.Message);
    }

    [Fact]
    public void Sem_coluna_obrigatoria_e_recusado()
    {
        var bytes = Xlsx.Write([]);
        Assert.Empty(Xlsx.Read(bytes, "r.xlsx"));
        Assert.Throws<ReviewImportException>(() => Xlsx.Read(XlsxTestFiles.WithHeader("id", "translation"), "r.xlsx"));
    }

    [Fact]
    public void Ignora_o_furigana_gravado_pelo_excel_japones_nas_strings_compartilhadas()
    {
        var bytes = XlsxTestFiles.WithSharedPhonetic("id", "translation", "status", "notes", "漢字", "かんじ");

        var lida = Assert.Single(Xlsx.Read(bytes, "r.xlsx"));

        Assert.Equal("漢字", lida.Translation);
    }

    [Fact]
    public void Referencia_de_celula_alem_da_ultima_coluna_e_recusada_sem_alocar_a_linha()
    {
        var bytes = XlsxTestFiles.WithHeader("id", "translation", "status", "notes");
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, true))
        {
            var cell = doc.WorkbookPart!.WorksheetParts.First().Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().First();
            cell.CellReference = "ZZZZZZZ1";
            doc.WorkbookPart.WorksheetParts.First().Worksheet!.Save();
        }

        Assert.Throws<ReviewImportException>(() => Xlsx.Read(stream.ToArray(), "r.xlsx"));
    }
}
