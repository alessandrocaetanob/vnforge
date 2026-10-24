using Spectre.Console;
using Spectre.Console.Rendering;

namespace VNForge.Cli;

/// <summary>Tabela e painel no estilo da CLI, que C2, C3 e C4 reaproveitam.</summary>
public static class CliViews
{
    /// <summary>Coluna de tabela; <paramref name="right"/> alinha à direita (números e percentuais).</summary>
    public static TableColumn Column(string header, bool right = false)
    {
        var column = new TableColumn($"[{CliPalette.Hex(CliPalette.Accent)}]{Escape(header)}[/]");
        if (right)
        {
            column.RightAligned();
        }

        return column;
    }

    /// <summary>Tabela sem moldura, só com o cabeçalho em destaque: lê bem no terminal e em texto simples.</summary>
    public static Table NewTable(params TableColumn[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var table = new Table { Border = TableBorder.None, ShowHeaders = true };
        table.AddColumns(columns);
        return table;
    }

    /// <summary>Painel com título, borda em destaque e conteúdo livre (resumo de build, resultado de uma operação).</summary>
    public static Panel NewPanel(string title, IRenderable content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new Panel(content)
        {
            Header = new PanelHeader($"[{CliPalette.Hex(CliPalette.Accent)}]{Escape(title)}[/]"),
            Border = BoxBorder.Rounded,
            Expand = false,
            BorderStyle = new Style(CliPalette.Accent),
            Padding = new Padding(1, 0),
        };
    }

    /// <summary>Protege texto de dados (nome de script, linha traduzida) para o markup não tomar <c>[IA]</c> por estilo.</summary>
    public static string Escape(string text) => Markup.Escape(text);
}
