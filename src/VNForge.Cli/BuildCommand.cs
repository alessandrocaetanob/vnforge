using System.CommandLine;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary><c>vnf build</c>: reconstrói os scripts com as traduções de <c>text/*.json</c> e grava os pacotes novos em <c>build/</c>, sem alterar a pasta do jogo.</summary>
internal static class BuildCommand
{
    private const int MaxWarningsShown = 10;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Option<bool> ReportOption = new("--report")
    {
        Description = "Só lista os caracteres que a estratégia de codificação troca ou não representa; não reconstrói nada. Sai com 2 se algum não for codificável.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("build", "Reconstrói os scripts com as traduções de text/ e grava os pacotes novos em build/ (não altera a pasta do jogo).");
        command.Options.Add(ReportOption);
        command.SetAction((parseResult, token) => Task.FromResult(Run(cli, parseResult, parseResult.InvocationConfiguration.Output, token)));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        if (parseResult.GetValue(ReportOption))
        {
            return RunReport(parseResult.GetValue(cli.Options.Json), ProjectBuilder.ReportEncoding(project), output);
        }

        var result = ProjectBuilder.Build(project, KnownEngines.All, token);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                buildDirectory = result.BuildDirectory,
                archives = result.Archives.Select(a => new { path = a.GamePath, replacedFiles = a.ReplacedFiles }),
                scripts = result.RebuiltScripts.Count,
                totalScripts = result.ScriptCount,
                lines = result.TotalLines,
                rebuilt = result.RebuiltScripts.Select(s => new { key = s.Key, name = s.Name, lines = s.Lines }),
                warnings = result.Warnings,
                encoding = EncodingData(result.Encoding),
            }));
            return ErrorHandler.OkExitCode;
        }

        foreach (var archive in result.Archives)
        {
            var path = Path.Combine(result.BuildDirectory, archive.GamePath.Replace('/', Path.DirectorySeparatorChar));
            var detail = archive.ReplacedFiles == 0
                ? "sem alterações, idêntico ao original"
                : $"{Number(archive.ReplacedFiles)} {Plural(archive.ReplacedFiles, "script alterado", "scripts alterados")}";
            output.WriteLine($"Gravado: {path} ({detail})");
        }

        output.WriteLine(Summary(result));
        if (result.Encoding.Replaced.Count > 0)
        {
            output.WriteLine($"Codificação '{result.Encoding.Strategy}': {Number(result.Encoding.Replaced.Count)} {Plural(result.Encoding.Replaced.Count, "caractere trocado", "caracteres trocados")}; vnf build --report lista cada um.");
        }

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

    private static int RunReport(bool json, EncodingReport report, TextWriter output)
    {
        var exitCode = report.HasBlocking ? ErrorHandler.QaBlockedExitCode : ErrorHandler.OkExitCode;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(EncodingData(report), exitCode));
            return exitCode;
        }

        output.WriteLine($"Estratégia de codificação '{report.Strategy}': {Number(report.CheckedLines)} {Plural(report.CheckedLines, "linha conferida", "linhas conferidas")}.");
        foreach (var issue in report.Issues)
        {
            var outcome = issue.Replacement is null ? "não codificável" : $"vira '{issue.Replacement}'";
            output.WriteLine($"{issue.UnitId} posição {issue.Position}: '{issue.Character}' ({issue.CodePoint}) {outcome}");
        }

        var blocking = report.Blocking.Count;
        var replaced = report.Replaced.Count;
        output.WriteLine($"{Number(blocking)} {Plural(blocking, "caractere não codificável", "caracteres não codificáveis")}; {Number(replaced)} {Plural(replaced, "caractere trocado", "caracteres trocados")}.");
        return exitCode;
    }

    private static object EncodingData(EncodingReport report) => new
    {
        strategy = report.Strategy,
        checkedLines = report.CheckedLines,
        unencodable = report.Blocking.Count,
        replaced = report.Replaced.Count,
        issues = report.Issues.Select(i => new { unit = i.UnitId, script = i.ScriptKey, position = i.Position, character = i.Character, codePoint = i.CodePoint, replacement = i.Replacement }),
    };

    private static string Summary(BuildResult result)
    {
        var scripts = result.RebuiltScripts.Count;
        var lines = result.TotalLines;
        var packages = result.Archives.Count;
        return $"Reconstruídos {Number(scripts)} {Plural(scripts, "script", "scripts")} ({Number(lines)} {Plural(lines, "linha trocada", "linhas trocadas")}); "
            + $"{Number(packages)} {Plural(packages, "pacote gravado", "pacotes gravados")} em build/";
    }

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;
}
