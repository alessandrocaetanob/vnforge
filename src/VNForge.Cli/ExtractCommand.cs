using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary><c>vnf extract</c>: descompacta os pacotes do jogo para <c>.cache/</c> e gera <c>text/*.json</c>, sem alterar a pasta do jogo.</summary>
internal static class ExtractCommand
{
    private const int MaxWarningsShown = 10;

    private static readonly TextUnitKind[] SummaryOrder =
        [TextUnitKind.Dialogue, TextUnitKind.Narration, TextUnitKind.Choice, TextUnitKind.Name, TextUnitKind.System, TextUnitKind.Other];

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    public static Command Create(VnfCli cli)
    {
        var command = new Command("extract", "Descompacta os arquivos do jogo para .cache/ e gera text/*.json (primeira extração; não altera a pasta do jogo).");
        command.SetAction((parseResult, token) => Task.FromResult(Run(cli, parseResult, parseResult.InvocationConfiguration.Output, token)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var result = ProjectExtractor.Extract(project, KnownEngines.All, token);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                cacheDirectory = result.CacheDirectory,
                archives = result.Archives.Select(a => new { path = a.GamePath, directory = a.Directory, files = a.FileCount, bytes = a.ByteCount }),
                scripts = result.Scripts.Count,
                lines = result.TotalLines,
                kinds = result.LinesByKind.ToDictionary(k => KindId(k.Key), k => k.Value),
                texts = result.Scripts.Select(s => new { key = s.Key, name = s.Name, lines = s.Lines }),
                warnings = result.Warnings,
            }));
            return ErrorHandler.OkExitCode;
        }

        foreach (var archive in result.Archives)
        {
            output.WriteLine($"Descompactado: {archive.GamePath} ({Number(archive.FileCount)} {Plural(archive.FileCount, "arquivo", "arquivos")}) em {Path.Combine(result.CacheDirectory, archive.Directory.Replace('/', Path.DirectorySeparatorChar))}");
        }

        output.WriteLine(Summary(result));
        foreach (var warning in result.Warnings.Take(MaxWarningsShown))
        {
            output.WriteLine($"Aviso: {warning}");
        }

        if (result.Warnings.Count > MaxWarningsShown)
        {
            output.WriteLine($"... e mais {Number(result.Warnings.Count - MaxWarningsShown)} aviso(s); use --json para ver todos.");
        }

        return ErrorHandler.OkExitCode;
    }

    private static string Summary(ExtractionResult result)
    {
        var scripts = result.Scripts.Count;
        var text = $"Extraídos {Number(scripts)} {Plural(scripts, "script", "scripts")}, {Number(result.TotalLines)} {Plural(result.TotalLines, "linha", "linhas")}";
        var kinds = SummaryOrder
            .Where(kind => result.LinesByKind.GetValueOrDefault(kind) > 0)
            .Select(kind => $"{Number(result.LinesByKind[kind])} {KindLabel(kind, result.LinesByKind[kind])}")
            .ToList();
        return kinds.Count == 0 ? text : $"{text} ({string.Join(", ", kinds)})";
    }

    private static string KindLabel(TextUnitKind kind, int count) => kind switch
    {
        TextUnitKind.Dialogue => Plural(count, "diálogo", "diálogos"),
        TextUnitKind.Narration => "narração",
        TextUnitKind.Name => Plural(count, "nome", "nomes"),
        TextUnitKind.Choice => Plural(count, "escolha", "escolhas"),
        TextUnitKind.System => "sistema",
        _ => Plural(count, "outro", "outros"),
    };

    private static string KindId(TextUnitKind kind) => kind switch
    {
        TextUnitKind.Dialogue => "dialogue",
        TextUnitKind.Narration => "narration",
        TextUnitKind.Name => "name",
        TextUnitKind.Choice => "choice",
        TextUnitKind.System => "system",
        _ => "other",
    };

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;
}
