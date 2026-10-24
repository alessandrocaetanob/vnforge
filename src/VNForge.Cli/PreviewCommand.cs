using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf preview &lt;id&gt;</c>: a tradução de uma linha depois do pipeline de formatação, com a contagem de linhas e de colunas.</summary>
internal static class PreviewCommand
{
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

        WriteText(cli.Ansi, preview);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteText(IAnsiConsole ansi, LinePreview preview)
    {
        ansi.MarkupLine($"[bold {CliPalette.Hex(CliPalette.Accent)}]Linha {CliViews.Escape(preview.Id)}[/]");
        if (preview.Notice is { } notice)
        {
            ansi.MarkupLine(CliViews.Escape(notice));
        }

        for (var b = 0; b < preview.Boxes.Count; b++)
        {
            ansi.WriteLine();
            if (preview.Speaker is { } speaker)
            {
                ansi.Write(TextboxView.Plate(speaker));
                ansi.WriteLine();
            }

            ansi.Write(TextboxView.Box(preview, b));
        }

        ansi.WriteLine();
        ansi.MarkupLine(
            $"Total: {Count(preview.LineCount, "linha", "linhas")}; largura máxima {CliFormat.Number(preview.MaxWidth)} colunas"
            + (preview.WrapEnabled ? "." : " (quebra desligada no perfil)."));
        foreach (var warning in preview.Warnings)
        {
            ansi.MarkupLine($"[{CliPalette.Hex(CliPalette.Warning)}]Aviso:[/] {CliViews.Escape(warning)}");
        }
    }

    private static string Count(int value, string singular, string plural) => CliFormat.Count(value, singular, plural);
}
