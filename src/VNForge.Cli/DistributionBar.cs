using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// Barra de distribuição por status, nas cores do app. Cada status tem também um glifo próprio, então a barra
/// continua legível sem cor (saída redirecionada, <c>--no-color</c>).
/// </summary>
public static class DistributionBar
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Status na ordem da barra: do mais adiantado ao pendente.</summary>
    private static readonly StatusStyle[] Styles =
    [
        new("Aprovadas", CliPalette.Approved, '█'),
        new("Revisadas", CliPalette.Reviewed, '▓'),
        new("Traduzidas", CliPalette.Translated, '▒'),
        new("IA", CliPalette.Ai, '░'),
        new("Pendentes", CliPalette.Pending, '·'),
    ];

    /// <summary>
    /// Divide <paramref name="width"/> células entre os status, na ordem aprovadas, revisadas, traduzidas, IA e
    /// pendentes. A soma é sempre <paramref name="width"/>; status com linhas ganha ao menos uma célula, para não sumir
    /// da barra; sem nenhuma linha, a barra inteira é pendente.
    /// </summary>
    public static int[] Allocate(IReadOnlyList<int> counts, int width)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentOutOfRangeException.ThrowIfNegative(width);

        var cells = new int[counts.Count];
        var total = counts.Sum();
        if (total == 0)
        {
            if (cells.Length > 0)
            {
                cells[^1] = width;
            }

            return cells;
        }

        var nonZero = counts.Count(c => c > 0);
        var remainders = new double[counts.Count];
        for (var i = 0; i < counts.Count; i++)
        {
            var exact = (double)counts[i] * width / total;
            cells[i] = (int)Math.Floor(exact);
            remainders[i] = exact - cells[i];
            if (counts[i] > 0 && cells[i] == 0 && width >= nonZero)
            {
                cells[i] = 1;
                remainders[i] = -1;
            }
        }

        var diff = width - cells.Sum();
        while (diff != 0)
        {
            if (diff > 0)
            {
                var i = LargestRemainder(remainders, counts);
                cells[i]++;
                remainders[i] -= 1;
                diff--;
            }
            else
            {
                var i = LargestCell(cells, counts);
                cells[i]--;
                diff++;
            }
        }

        return cells;
    }

    /// <summary>Barra de <paramref name="width"/> células com as linhas de <paramref name="counts"/>.</summary>
    public static IRenderable Create(StatusCounts counts, int width)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var cells = Allocate(Values(counts), width);
        var markup = new StringBuilder();
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] > 0)
            {
                var style = Styles[i];
                markup.Append('[').Append(CliPalette.Hex(style.Color)).Append(']')
                    .Append(style.Glyph, cells[i])
                    .Append("[/]");
            }
        }

        return new Markup(markup.ToString());
    }

    /// <summary>Legenda <c>█ Aprovadas 34  ▓ Revisadas 36 ...</c> com os mesmos glifos e cores da barra.</summary>
    public static IRenderable Legend(StatusCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        var values = Values(counts);
        var items = Styles.Select((style, i) =>
            $"[{CliPalette.Hex(style.Color)}]{style.Glyph}[/] {style.Label} {values[i].ToString("N0", PortugueseBrazil)}");
        return new Markup(string.Join("  ", items));
    }

    private static int[] Values(StatusCounts counts) =>
        [counts.Approved, counts.Reviewed, counts.Translated, counts.Machine, counts.Untranslated];

    // Maior sobra de arredondamento entre os status com linhas.
    private static int LargestRemainder(double[] remainders, IReadOnlyList<int> counts)
    {
        var best = -1;
        for (var i = 0; i < remainders.Length; i++)
        {
            if (counts[i] > 0 && (best < 0 || remainders[i] > remainders[best]))
            {
                best = i;
            }
        }

        return best;
    }

    // Status com mais de uma célula e mais células: o que menos sente perder uma.
    private static int LargestCell(int[] cells, IReadOnlyList<int> counts)
    {
        var best = -1;
        for (var i = 0; i < cells.Length; i++)
        {
            var floor = counts[i] > 0 ? 1 : 0;
            if (cells[i] > floor && (best < 0 || cells[i] > cells[best]))
            {
                best = i;
            }
        }

        return best;
    }

    private sealed record StatusStyle(string Label, Color Color, char Glyph);
}
