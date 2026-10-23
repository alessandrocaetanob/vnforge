using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf preview &lt;id&gt;</c>: a tradução de uma linha depois do pipeline de formatação, com a contagem de linhas e de colunas.</summary>
internal static class PreviewCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Argument<string> IdArgument = new("id")
    {
        Description = "Id da linha, como aparece em text/*.json.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("preview", "Mostra como a linha fica depois da formatação e da quebra de linha, com linhas e colunas.");
        command.Arguments.Add(IdArgument);
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.InvocationConfiguration.Output));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var preview = LinePreviewer.Create(project, parseResult.GetRequiredValue(IdArgument));

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                id = preview.Id,
                source = preview.Source,
                translation = preview.Translation,
                text = preview.Text,
                formatted = preview.Formatted,
                lines = preview.LineCount,
                maxWidth = preview.MaxWidth,
                maxLines = preview.MaxLines,
                wrapEnabled = preview.WrapEnabled,
                boxes = preview.Columns.Select(box => new { lines = box.Count, columns = box }),
                warnings = preview.Warnings,
            }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine($"Linha {preview.Id}");
        if (!preview.Formatted)
        {
            output.WriteLine("O build não formata esta linha (papel fora de kinds do formatting.json ou tradução igual ao original); ela é gravada como está.");
        }

        for (var b = 0; b < preview.Columns.Count; b++)
        {
            var box = preview.Columns[b];
            output.WriteLine();
            output.WriteLine($"Caixa {b + 1}: {Count(box.Count, "linha", "linhas")}");
            foreach (var (columns, n) in box.Select((c, n) => (c, n)))
            {
                output.WriteLine($"  {n + 1}: {Count(columns, "coluna", "colunas")}");
            }
        }

        output.WriteLine();
        output.WriteLine("Texto:");
        foreach (var line in preview.Text.Replace("\\p", "\\p\\n", StringComparison.Ordinal).Split("\\n"))
        {
            output.WriteLine($"  {line}");
        }

        output.WriteLine();
        output.WriteLine(
            $"Total: {Count(preview.LineCount, "linha", "linhas")}; largura máxima {Number(preview.MaxWidth)} colunas"
            + (preview.WrapEnabled ? "." : " (quebra desligada no perfil)."));
        foreach (var warning in preview.Warnings)
        {
            output.WriteLine($"Aviso: {warning}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static string Count(int value, string singular, string plural) => $"{Number(value)} {(value == 1 ? singular : plural)}";

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);
}
