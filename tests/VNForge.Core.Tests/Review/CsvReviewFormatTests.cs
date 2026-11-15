using System.Text;
using VNForge.Core.Review;

namespace VNForge.Core.Tests.Review;

[Trait("Risco", "R3")]
public class CsvReviewFormatTests
{
    private static ReviewRow Linha(string id, string translation = "", string notes = "", string source = "原文", string speaker = "") =>
        new(0, id, "dialogue", speaker, source, translation, "translated", notes);

    private static IReadOnlyList<ReviewRow> IdaEVolta(params ReviewRow[] linhas) =>
        ReviewFormats.Csv.Read(ReviewFormats.Csv.Write(linhas), "x.csv");

    [Fact]
    public void Ida_e_volta_preserva_virgula_aspas_quebra_de_linha_e_marcadores()
    {
        var original = Linha("a:1", "ele disse \"oi\", e\nfoi\\p", notes: "linha1\r\nlinha2");

        var lida = Assert.Single(IdaEVolta(original));

        Assert.Equal(original.Translation, lida.Translation);
        Assert.Equal(original.Notes, lida.Notes);
        Assert.Equal("a:1", lida.Id);
    }

    [Theory]
    [InlineData("id,translation,status\r\na:1,t,translated\r\n", "notes")]
    public void Cabecalho_sem_coluna_obrigatoria_e_erro(string csv, string coluna)
    {
        var ex = Assert.Throws<ReviewImportException>(() => ReviewFormats.Csv.Read(Encoding.UTF8.GetBytes(csv), "x.csv"));

        Assert.Contains($"'{coluna}'", ex.Message);
    }
}
