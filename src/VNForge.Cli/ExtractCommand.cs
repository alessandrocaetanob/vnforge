using System.CommandLine;
using System.Diagnostics;
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
        var command = new Command("extract", "Descompacta os arquivos do jogo para .cache/ e gera text/*.json (repetido, mantém as traduções; não altera a pasta do jogo).");
        command.SetAction((parseResult, token) => RunAsync(cli, parseResult, parseResult.InvocationConfiguration.Output, token));
        return command;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, TextWriter output, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var clock = Stopwatch.StartNew();
        var result = await LiveProgress.RunAsync(
            cli.Ansi,
            cli.CanAnimate,
            "Descompactando o jogo",
            total: null,
            sink => Task.FromResult(ProjectExtractor.Extract(
                project,
                KnownEngines.All,
                token,
                new InlineProgress<ScriptWorkProgress>(progress => sink.Scripts(progress, "gravando text/"))))).ConfigureAwait(false);

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
                reextraction = result.Reextraction is { } re ? new { kept = re.Kept, outdated = re.Outdated, added = re.Added, removed = re.Removed } : null,
                warnings = result.Warnings,
            }));
            return ErrorHandler.OkExitCode;
        }

        if (cli.RichOutput)
        {
            cli.Ansi.Write(RunSummaries.ExtractPanel(result, clock.Elapsed));
            WriteWarnings(output, result);
            return ErrorHandler.OkExitCode;
        }

        foreach (var archive in result.Archives)
        {
            output.WriteLine($"Descompactado: {archive.GamePath} ({Number(archive.FileCount)} {Plural(archive.FileCount, "arquivo", "arquivos")}) em {Path.Combine(result.CacheDirectory, archive.Directory.Replace('/', Path.DirectorySeparatorChar))}");
        }

        output.WriteLine(Summary(result));
        if (result.Reextraction is { } r)
        {
            output.WriteLine($"Re-extração: {Number(r.Kept)} mantidas, {Number(r.Outdated)} desatualizadas (original mudou; tradução antiga guardada), {Number(r.Added)} novas, {Number(r.Removed)} removidas.");
        }

        WriteWarnings(output, result);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteWarnings(TextWriter output, ExtractionResult result)
    {
        foreach (var warning in result.Warnings.Take(MaxWarningsShown))
        {
            output.WriteLine($"Aviso: {warning}");
        }

        if (result.Warnings.Count > MaxWarningsShown)
        {
            output.WriteLine($"... e mais {Number(result.Warnings.Count - MaxWarningsShown)} aviso(s); use --json para ver todos.");
        }
    }

    private static string Summary(ExtractionResult result)
    {
        var scripts = result.Scripts.Count;
        var text = $"Extraídos {Number(scripts)} {Plural(scripts, "script", "scripts")}, {Number(result.TotalLines)} {Plural(result.TotalLines, "linha", "linhas")}";
        var kinds = KindsText(result);
        return kinds.Length == 0 ? text : $"{text} ({kinds})";
    }

    /// <summary>"1.204 diálogos, 38 narração, 6 escolhas": as linhas por papel, sem os papéis vazios.</summary>
    internal static string KindsText(ExtractionResult result) =>
        string.Join(
            ", ",
            SummaryOrder
                .Where(kind => result.LinesByKind.GetValueOrDefault(kind) > 0)
                .Select(kind => $"{Number(result.LinesByKind[kind])} {KindLabel(kind, result.LinesByKind[kind])}"));

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
