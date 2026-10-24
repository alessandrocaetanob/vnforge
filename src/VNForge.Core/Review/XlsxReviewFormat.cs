using System.IO.Packaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace VNForge.Core.Review;

/// <summary>
/// Planilha XLSX (Open XML, pelo <c>DocumentFormat.OpenXml</c>) com uma aba <c>Revisao</c>: linha 1 com o cabeçalho
/// <c>id,kind,speaker,source,translation,status,notes</c> e uma linha por unidade. Toda célula é texto (<c>inlineStr</c>), então o Excel
/// não converte ids nem trata <c>=</c> como fórmula, e o Unicode (japonês) é gravado em UTF-8 pelo próprio formato. Na leitura aceita
/// colunas em qualquer ordem, colunas extras, texto inline ou em <c>sharedStrings</c> e números; só <c>id</c>, <c>translation</c>,
/// <c>status</c> e <c>notes</c> são obrigatórias. Lê a primeira aba.
/// </summary>
internal sealed partial class XlsxReviewFormat : IReviewFormat
{
    private static readonly string[] Header = ["id", "kind", "speaker", "source", "translation", "status", "notes"];
    private static readonly string[] Required = ["id", "translation", "status", "notes"];

    public string Name => "xlsx";

    public string Extension => ".xlsx";

    public byte[] Write(IReadOnlyList<ReviewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sheetData = new SheetData();
        sheetData.Append(MakeRow(1, Header));
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            sheetData.Append(MakeRow((uint)(i + 2), [r.Id, r.Kind, r.Speaker, r.Source, r.Translation, r.Status, r.Notes]));
        }

        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var book = doc.AddWorkbookPart();
            book.Workbook = new Workbook(new Sheets());
            var sheet = book.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new Worksheet(sheetData);
            book.Workbook.Sheets!.Append(new Sheet { Id = book.GetIdOfPart(sheet), SheetId = 1, Name = "Revisao" });
        }

        return stream.ToArray();
    }

    public IReadOnlyList<ReviewRow> Read(byte[] data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data);
            using var doc = SpreadsheetDocument.Open(stream, false);
            return ReadSheet(doc, path);
        }
        catch (Exception e) when (e is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException or ArgumentException or XmlException)
        {
            throw new ReviewImportException($"'{path}' não é uma planilha XLSX válida ({e.Message}); salve o arquivo como \"Pasta de Trabalho do Excel (.xlsx)\".");
        }
    }

    private static Row MakeRow(uint index, IReadOnlyList<string> values)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i].Length == 0)
            {
                continue;
            }

            row.Append(new Cell
            {
                CellReference = ColumnName(i) + index,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(Escape(values[i])) { Space = SpaceProcessingModeValues.Preserve }),
            });
        }

        return row;
    }

    private static List<ReviewRow> ReadSheet(SpreadsheetDocument doc, string path)
    {
        var book = doc.WorkbookPart ?? throw new ReviewImportException($"'{path}' não tem pasta de trabalho.");
        var sheet = book.WorksheetParts.FirstOrDefault()?.Worksheet ?? throw new ReviewImportException($"'{path}' não tem nenhuma aba.");
        var shared = book.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];

        var records = new List<(int Line, string[] Cells)>();
        foreach (var row in sheet.Descendants<Row>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : cells.Count;
                while (cells.Count <= column)
                {
                    cells.Add(string.Empty);
                }

                cells[column] = CellText(cell, shared);
            }

            if (cells.Any(c => c.Length > 0))
            {
                records.Add(((int)(row.RowIndex?.Value ?? (uint)records.Count + 1), [.. cells]));
            }
        }

        if (records.Count == 0)
        {
            throw new ReviewImportException($"'{path}' está vazio: falta a linha de cabeçalho ({string.Join(",", Header)}).");
        }

        var columns = records[0].Cells.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = Required.FirstOrDefault(r => !columns.Contains(r));
        if (missing is not null)
        {
            throw new ReviewImportException($"'{path}': falta a coluna '{missing}' no cabeçalho.");
        }

        string Cell(string[] cells, string name)
        {
            var index = columns.IndexOf(name);
            return index < 0 || index >= cells.Length ? string.Empty : cells[index];
        }

        return [.. records.Skip(1).Select(r => new ReviewRow(
            r.Line, Cell(r.Cells, "id").Trim(), Cell(r.Cells, "kind"), Cell(r.Cells, "speaker"), Cell(r.Cells, "source"),
            Cell(r.Cells, "translation"), Cell(r.Cells, "status"), Cell(r.Cells, "notes")))];
    }

    private static string CellText(Cell cell, List<string> shared)
    {
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString)
        {
            return Unescape(cell.InlineString?.InnerText ?? string.Empty);
        }

        var value = cell.CellValue?.Text ?? string.Empty;
        return type == CellValues.SharedString && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var i) && i >= 0 && i < shared.Count
            ? Unescape(shared[i])
            : value;
    }

    /// <summary>
    /// O XML normaliza CR e não aceita outros controles, então eles saem como <c>_xHHHH_</c> (a convenção do Excel para ST_Xstring), e um
    /// <c>_xHHHH_</c> que já estava no texto sai como <c>_x005F_xHHHH_</c>. <see cref="Unescape"/> desfaz.
    /// </summary>
    private static bool NeedsEscape(char c) => c < ' ' && c != '\t' && c != '\n';

    private static string Escape(string value)
    {
        if (!value.Any(NeedsEscape) && !value.Contains('_'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (NeedsEscape(c))
            {
                sb.Append($"_x{(int)c:X4}_");
            }
            else if (c == '_' && EscapeSequenceAtStart().IsMatch(value.AsSpan(i)))
            {
                sb.Append("_x005F_");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string Unescape(string value) =>
        value.Contains("_x", StringComparison.Ordinal)
            ? EscapeSequence().Replace(value, m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)).ToString())
            : value;

    [GeneratedRegex("^_x[0-9A-Fa-f]{4}_")]
    private static partial Regex EscapeSequenceAtStart();

    [GeneratedRegex("_x([0-9A-Fa-f]{4})_")]
    private static partial Regex EscapeSequence();

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsAsciiLetter))
        {
            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return Math.Max(index - 1, 0);
    }
}
