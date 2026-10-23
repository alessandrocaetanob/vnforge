using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Review;

namespace VNForge.Cli;

/// <summary><c>vnf export --format csv</c>: grava as linhas de <c>text/*.json</c> num arquivo para revisores. Não altera o projeto.</summary>
internal static class ExportCommand
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var format = new Option<string>("--format")
        {
            Description = $"Formato do arquivo: {string.Join(", ", ReviewFormats.All.Select(f => f.Name))}.",
            HelpName = "formato",
            DefaultValueFactory = _ => ReviewFormats.Csv.Name,
        };
        var output = new Option<string?>("--output")
        {
            Description = "Arquivo de destino (padrão: revisao.<formato> na pasta do projeto).",
            HelpName = "arquivo",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Troca o arquivo de destino se ele já existir.",
        };

        var command = new Command("export", "Exporta o texto de text/ para um arquivo de revisão externa (não altera o projeto).");
        command.Options.Add(format);
        command.Options.Add(output);
        command.Options.Add(force);
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.GetRequiredValue(format), parseResult.GetValue(output), parseResult.GetValue(force)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, string formatName, string? outputArgument, bool force)
    {
        var format = ReviewFormats.Find(formatName)
            ?? throw new UsageException($"Formato '{formatName}' desconhecido; os formatos de exportação são: {string.Join(", ", ReviewFormats.All.Select(f => f.Name))}.");
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var path = outputArgument is null
            ? Path.Combine(project.Root, "revisao" + format.Extension)
            : Path.GetFullPath(outputArgument, cli.CurrentDirectory);
        if (File.Exists(path) && !force)
        {
            throw new UsageException($"'{path}' já existe; use --force para trocá-lo ou --output para outro arquivo.");
        }

        var lines = ReviewExporter.Export(project, format, path);

        var writer = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            writer.WriteLine(JsonEnvelope.Result(new { format = format.Name, path, lines }));
            return ErrorHandler.OkExitCode;
        }

        writer.WriteLine($"Exportadas {lines.ToString("N0", PortugueseBrazil)} {(lines == 1 ? "linha" : "linhas")} em {path}");
        return ErrorHandler.OkExitCode;
    }
}
