using System.Text;

namespace VNForge.Core.Review;

/// <summary>
/// CSV (RFC 4180) em UTF-8 com BOM, para o Excel abrir o japonês direito (na leitura, aceita também o `;` que o Excel pt-BR grava): colunas <c>id,kind,speaker,source,translation,status,notes</c>,
/// campos com vírgula, aspas ou quebra de linha entre aspas e fim de linha CRLF. Na leitura aceita ou não o BOM, CRLF ou LF, colunas em
/// qualquer ordem e colunas extras; só <c>id</c>, <c>translation</c>, <c>status</c> e <c>notes</c> são obrigatórias.
/// </summary>
internal sealed class CsvReviewFormat : IReviewFormat
{
    private static readonly string[] Header = ["id", "kind", "speaker", "source", "translation", "status", "notes"];
    private static readonly string[] Required = ["id", "translation", "status", "notes"];

    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string Name => "csv";

    public string Extension => ".csv";

    public byte[] Write(IReadOnlyList<ReviewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        AppendRecord(sb, Header);
        foreach (var row in rows)
        {
            AppendRecord(sb, [row.Id, row.Kind, row.Speaker, row.Source, row.Translation, row.Status, row.Notes]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public IReadOnlyList<ReviewRow> Read(byte[] data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);

        string text;
        try
        {
            text = Utf8Estrito.GetString(data).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new ReviewImportException($"'{path}' não está em UTF-8 válido; salve o CSV como \"CSV UTF-8\".");
        }

        var headerLine = text.Split('\n', 2)[0];
        var records = Split(text, path, headerLine.Contains(';') && !headerLine.Contains(',') ? ';' : ',');
        if (records.Count == 0)
        {
            throw new ReviewImportException($"'{path}' está vazio: falta a linha de cabeçalho ({string.Join(",", Header)}).");
        }

        var columns = records[0].Fields.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = Required.FirstOrDefault(r => !columns.Contains(r));
        if (missing is not null)
        {
            throw new ReviewImportException($"'{path}': falta a coluna '{missing}' no cabeçalho.");
        }

        int Index(string name) => columns.IndexOf(name);
        var (id, kind, speaker, source, translation, status, notes) =
            (Index("id"), Index("kind"), Index("speaker"), Index("source"), Index("translation"), Index("status"), Index("notes"));
        string Cell(List<string> fields, int index) => index < 0 ? string.Empty : fields[index];

        var rows = new List<ReviewRow>(records.Count - 1);
        foreach (var (line, fields) in records.Skip(1))
        {
            if (fields.Count != columns.Count)
            {
                throw new ReviewImportException($"'{path}', linha {line}: {fields.Count} colunas, mas o cabeçalho tem {columns.Count}.");
            }

            rows.Add(new ReviewRow(line, Restore(Cell(fields, id).Trim()), Cell(fields, kind), Restore(Cell(fields, speaker)), Restore(Cell(fields, source)), Restore(Cell(fields, translation)), Cell(fields, status), Restore(Cell(fields, notes))));
        }

        return rows;
    }

    private static void AppendRecord(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendField(sb, fields[i]);
        }

        sb.Append("\r\n");
    }

    private static void AppendField(StringBuilder sb, string value)
    {
        value = Neutralize(value);
        if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            sb.Append(value);
            return;
        }

        sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }

    /// <summary>
    /// Impede que o Excel trate a célula como fórmula (CSV injection): texto que começa com <c>= + - @</c>, tab ou CR ganha um apóstrofo na frente.
    /// Texto que já começa com apóstrofos seguidos de um desses também ganha um, para <see cref="Restore"/> desfazer sem ambiguidade.
    /// </summary>
    private static string Neutralize(string value) => NeedsGuard(value) ? "'" + value : value;

    private static string Restore(string value) => value.StartsWith('\'') && NeedsGuard(value[1..]) ? value[1..] : value;

    private static bool NeedsGuard(string value)
    {
        var rest = value.TrimStart('\'');
        return rest.Length > 0 && rest[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
    }

    /// <summary>Divide o texto em registros, guardando a linha onde cada um começa. Linhas em branco somem.</summary>
    private static List<(int Line, List<string> Fields)> Split(string text, string path, char delimiter) =>
        new RecordSplitter(text, path, delimiter).Run();

    private sealed class RecordSplitter(string text, string path, char delimiter)
    {
        private readonly List<(int, List<string>)> _records = [];
        private readonly StringBuilder _field = new();
        private List<string> _fields = [];
        private int _line = 1;
        private int _recordLine = 1;
        private bool _wasQuoted;

        public List<(int Line, List<string> Fields)> Run()
        {
            var i = 0;
            while (i < text.Length)
            {
                i = (text[i] == '"' && _field.Length == 0 && !_wasQuoted
                    ? ReadQuoted(i + 1)
                    : ReadUnquoted(i)) + 1;
            }

            if (_field.Length > 0 || _fields.Count > 0 || _wasQuoted)
            {
                EndRecord();
            }

            return _records;
        }

        /// <summary>Lê um campo entre aspas a partir de <paramref name="start"/>; devolve o índice da aspa que fecha.</summary>
        private int ReadQuoted(int start)
        {
            _wasQuoted = true;
            var i = start;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    _field.Append('"');
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    return i;
                }

                _line += c == '\n' ? 1 : 0;
                _field.Append(c);
                i++;
            }

            throw new ReviewImportException($"'{path}': aspas abertas na linha {_recordLine} e nunca fechadas.");
        }

        private int ReadUnquoted(int i)
        {
            var c = text[i];
            if (c == '"')
            {
                throw new ReviewImportException($"'{path}', linha {_line}: aspas no meio de um campo; um campo com aspas deve ir todo entre aspas.");
            }

            if (c == delimiter)
            {
                _fields.Add(_field.ToString());
                _field.Clear();
                _wasQuoted = false;
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                // O \n seguinte encerra o registro.
            }
            else if (c is '\r' or '\n')
            {
                EndRecord();
                _line++;
                _recordLine = _line;
            }
            else if (_wasQuoted)
            {
                throw new ReviewImportException($"'{path}', linha {_line}: texto depois das aspas que fecham o campo.");
            }
            else
            {
                _field.Append(c);
            }

            return i;
        }

        private void EndRecord()
        {
            _fields.Add(_field.ToString());
            _field.Clear();
            if (!(_fields.Count == 1 && _fields[0].Length == 0 && !_wasQuoted))
            {
                _records.Add((_recordLine, _fields));
            }

            _fields = [];
            _wasQuoted = false;
        }
    }
}
