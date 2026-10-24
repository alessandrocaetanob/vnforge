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

    /// <summary>Cabeçalho mais uma linha cuja tradução é uma string compartilhada com furigana (<c>rPh</c>).</summary>
    public static byte[] WithSharedPhonetic(string id, string translation, string status, string notes, string text, string phonetic)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = doc.AddWorkbookPart();
            book.Workbook = new Workbook(new Sheets());
            var strings = book.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(text), new PhoneticRun(new DocumentFormat.OpenXml.Spreadsheet.Text(phonetic)) { BaseTextStartIndex = 0, EndingBaseIndex = 2 }));
            var sheetPart = book.AddNewPart<WorksheetPart>();
            var header = new Row { RowIndex = 1 };
            foreach (var h in new[] { id, translation, status, notes })
            {
                header.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(h)) });
            }

            var data = new Row { RowIndex = 2 };
            data.Append(new Cell { CellReference = "A2", DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("a:1")) });
            data.Append(new Cell { CellReference = "B2", DataType = CellValues.SharedString, CellValue = new CellValue("0") });
            data.Append(new Cell { CellReference = "C2", DataType = CellValues.InlineString, InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("machine")) });
            sheetPart.Worksheet = new Worksheet(new SheetData(header, data));
            book.Workbook.Sheets!.Append(new Sheet { Id = book.GetIdOfPart(sheetPart), SheetId = 1, Name = "Revisao" });
        }

        return stream.ToArray();
    }
}
