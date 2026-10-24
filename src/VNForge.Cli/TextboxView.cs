using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Formatting;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// A caixa de texto do jogo no terminal, como o app a desenha: fundo escuro, régua na coluna limite (<c>┊</c>) e o que passa
/// do limite em coral. Sem cor a régua e o separador continuam visíveis, então o excesso se lê igual.
/// </summary>
public static class TextboxView
{
    private const char Ruler = '┊';
    private const char Dash = '┄';

    /// <summary>Placa com o nome do falante, acima da caixa.</summary>
    public static IRenderable Plate(string speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);

        return new Markup($"[{CliPalette.Hex(CliPalette.TextboxInk)} on {CliPalette.Hex(CliPalette.TextboxPlate)}] {CliViews.Escape(speaker)} [/]");
    }

    /// <summary>A caixa de número <paramref name="index"/> (a partir de 0) de <paramref name="preview"/>, num painel com o total de linhas no título.</summary>
    public static IRenderable Box(LinePreview preview, int index)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var box = preview.Boxes[index];
        int? limit = preview.WrapEnabled ? preview.MaxWidth : null;
        int? maxLines = preview.Formatted ? preview.MaxLines : null;

        var rows = new List<Row>();
        for (var n = 0; n < box.Count; n++)
        {
            if (maxLines is { } max && n == max)
            {
                rows.Add(Separator(max));
            }

            rows.Add(Line(box[n], limit, overflow: maxLines is { } m && n >= m));
        }

        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Cells);
        var background = CliPalette.Hex(CliPalette.TextboxBackground);
        var ink = CliPalette.Hex(CliPalette.TextboxInk);
        var content = new Rows(rows.Select(row => (IRenderable)new Markup(row.Render(width, ink, background))));

        return CliViews.NewPanel($"Caixa {index + 1} · {CliFormat.Count(box.Count, "linha", "linhas")}", content);
    }

    private static Row Line(MeasuredLine line, int? limit, bool overflow)
    {
        var overflowColor = CliPalette.Hex(CliPalette.TextboxOverflow);
        var text = new StringBuilder();
        var cells = 0;
        var column = 0;
        var rulerDrawn = limit is null;

        void DrawRuler()
        {
            var pad = Math.Max(0, limit!.Value - column);
            text.Append(' ', pad).Append(Ruler);
            cells += pad + 1;
            rulerDrawn = true;
        }

        foreach (var segment in line.Segments)
        {
            if (segment.IsMarker)
            {
                text.Append("[dim]").Append(CliViews.Escape(segment.Text)).Append("[/]");
                cells += segment.Text.Length;
                continue;
            }

            var beyond = limit is { } l && segment.Column + segment.Width > l;
            if (!rulerDrawn && beyond)
            {
                DrawRuler();
            }

            var glyph = CliViews.Escape(segment.Text);
            text.Append(overflow || beyond ? $"[{overflowColor}]{glyph}[/]" : glyph);
            cells += segment.Width;
            column = segment.Column + segment.Width;
        }

        if (!rulerDrawn)
        {
            DrawRuler();
        }

        var counter = limit is { } width ? $"{line.Columns}/{width} col" : $"{line.Columns} col";
        var note = overflow ? $"  [{overflowColor}]acima do limite[/]" : string.Empty;
        return new Row(text.ToString(), cells, $"  [dim]{counter}[/]{note}");
    }

    private static Row Separator(int maxLines)
    {
        var color = CliPalette.Hex(CliPalette.TextboxOverflow);
        return new Row(string.Empty, 0, $"  [{color}]limite de {CliFormat.Count(maxLines, "linha", "linhas")}[/]", Dash);
    }

    // Texto já com markup e quantas células do terminal ele ocupa (marcadores contam o próprio texto, glifos a largura da tabela do jogo).
    private sealed record Row(string Text, int Cells, string Suffix, char Fill = ' ')
    {
        public string Render(int width, string ink, string background)
        {
            var pad = new string(Fill, Math.Max(0, width - Cells));
            var body = Fill == ' ' ? $" {Text}{pad} " : $"{new string(Fill, width + 2)}";
            var color = Fill == ' ' ? ink : CliPalette.Hex(CliPalette.TextboxOverflow);
            return $"[{color} on {background}]{body}[/]{Suffix}";
        }
    }
}
