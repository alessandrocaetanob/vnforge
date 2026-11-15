using VNForge.Core.Review;

namespace VNForge.Core.Tests.Review;

[Trait("Risco", "R3")]
public class XlsxReviewFormatTests
{
    private static readonly IReviewFormat Xlsx = ReviewFormats.Xlsx;

    private static ReviewRow Linha(string id, string source = "おはよう", string translation = "Bom dia", string notes = "") =>
        new(0, id, "dialogue", "美咲", source, translation, "machine", notes);

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
    public void Sem_coluna_obrigatoria_e_recusado()
    {
        var bytes = Xlsx.Write([]);
        Assert.Empty(Xlsx.Read(bytes, "r.xlsx"));
        Assert.Throws<ReviewImportException>(() => Xlsx.Read(XlsxTestFiles.WithHeader("id", "translation"), "r.xlsx"));
    }
}
