using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace VNForge.Core.Tests.Review;

internal static class XlsxTestFiles
{
    /// <summary>Planilha mínima só com a linha de cabeçalho dada, como um revisor ou outra ferramenta a gravaria.</summary>
    public static byte[] WithHeader(params string[] headers)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = doc.AddWorkbookPart();
            book.Workbook = new Workbook(new Sheets());
            var sheetPart = book.AddNewPart<WorksheetPart>();
            var row = new Row();
            foreach (var h in headers)
            {
                row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(h)) });
            }

            sheetPart.Worksheet = new Worksheet(new SheetData(row));
            book.Workbook.Sheets!.Append(new Sheet { Id = book.GetIdOfPart(sheetPart), SheetId = 1, Name = "Revisao" });
        }

        return stream.ToArray();
    }
}
