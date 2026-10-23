using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Review;

namespace VNForge.Cli;

/// <summary><c>vnf import &lt;arquivo&gt;</c>: aplica um arquivo de revisão a <c>text/*.json</c>, casando por id. Só tradução, status e notas mudam; com qualquer erro nada é importado.</summary>
internal static class ImportCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var file = new Argument<string>("arquivo") { Description = "Arquivo de revisão exportado por vnf export." };
        var format = new Option<string?>("--format")
        {
            Description = $"Formato do arquivo: {string.Join(", ", ReviewFormats.All.Select(f => f.Name))} (padrão: pela extensão).",
            HelpName = "formato",
        };

        var command = new Command("import", "Importa um arquivo de revisão para text/, casando por id (só tradução, status e notas mudam; com qualquer erro nada é importado).");
        command.Arguments.Add(file);
        command.Options.Add(format);
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.GetRequiredValue(file), parseResult.GetValue(format)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, string fileArgument, string? formatName)
    {
        var format = formatName is null
            ? null
            : ReviewFormats.Find(formatName)
                ?? throw new UsageException($"Formato '{formatName}' desconhecido; os formatos de importação são: {string.Join(", ", ReviewFormats.All.Select(f => f.Name))}.");
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));

        var result = ReviewImporter.Import(project, Path.GetFullPath(fileArgument, cli.CurrentDirectory), format);

        var writer = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            writer.WriteLine(JsonEnvelope.Result(new { rows = result.Rows, changed = result.ChangedUnits, scripts = result.ChangedScripts }));
            return ErrorHandler.OkExitCode;
        }

        var changed = result.ChangedUnits;
        writer.WriteLine(changed == 0
            ? $"Nenhuma alteração: as {result.Rows.ToString("N0", PortugueseBrazil)} linhas do arquivo já estão iguais ao projeto."
            : $"Importadas {changed.ToString("N0", PortugueseBrazil)} {(changed == 1 ? "linha alterada" : "linhas alteradas")} em {result.ChangedScripts.Count.ToString("N0", PortugueseBrazil)} {(result.ChangedScripts.Count == 1 ? "script" : "scripts")} (de {result.Rows.ToString("N0", PortugueseBrazil)} no arquivo).");
        return ErrorHandler.OkExitCode;
    }
}
