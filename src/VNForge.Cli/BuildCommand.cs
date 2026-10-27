using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Cli;

/// <summary><c>vnf build</c>: reconstrói os scripts com as traduções de <c>text/*.json</c> e grava os pacotes novos em <c>build/</c>, sem alterar a pasta do jogo.</summary>
internal static class BuildCommand
{
    private const int MaxWarningsShown = 10;

    private const int MaxQaIssuesShown = 50;

    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Option<bool> ReportOption = new("--report")
    {
        Description = "Só lista os caracteres que a estratégia de codificação troca ou não representa; não reconstrói nada. Sai com 2 se algum não for codificável.",
    };

    private static readonly Option<bool> AllowWarningsOption = new("--allow-warnings")
    {
        Description = "Deixa o build seguir com avisos de QA (os não marcados como ok); erros de QA continuam bloqueando e saem com 2.",
    };

    private static readonly Option<bool> FullOption = new("--full")
    {
        Description = "Reconstrói todos os scripts; sem a opção o build reaproveita os que não mudaram desde o build anterior.",
    };

    private static readonly Option<string?> EncodingOption = new("--encoding")
    {
        Description = "Estratégia de codificação só deste build (sjis, fold, cp1252); sem a opção vale o encoding.strategy do vnforge.json, que não é alterado.",
        HelpName = "estratégia",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("build", "Reconstrói os scripts com as traduções de text/ e grava os pacotes em build/, sem tocar no jogo.");
        command.Options.Add(ReportOption);
        command.Options.Add(AllowWarningsOption);
        command.Options.Add(FullOption);
        command.Options.Add(EncodingOption);
        command.SetAction((parseResult, token) => RunAsync(cli, parseResult, parseResult.InvocationConfiguration.Output, token));
        return command;
    }

    private static async Task<int> RunAsync(VnfCli cli, ParseResult parseResult, TextWriter output, CancellationToken token)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        if (parseResult.GetValue(ReportOption))
        {
            return RunReport(parseResult.GetValue(cli.Options.Json), ProjectBuilder.ReportEncoding(project, encodingStrategy: parseResult.GetValue(EncodingOption)), output);
        }

        var json = parseResult.GetValue(cli.Options.Json);
        var allowWarnings = parseResult.GetValue(AllowWarningsOption);
        var options = new BuildOptions(OnlyChanged: !parseResult.GetValue(FullOption), parseResult.GetValue(EncodingOption));
        var clock = Stopwatch.StartNew();
        BuildResult result;
        try
        {
            result = await LiveProgress.RunAsync(
                cli.Ansi,
                cli.CanAnimate,
                "Conferindo as traduções",
                total: null,
                sink => Task.FromResult(ProjectBuilder.Build(
                    project,
                    KnownEngines.All,
                    qa: cli.Qa,
                    allowWarnings: allowWarnings,
                    options: options,
                    cancellationToken: token,
                    progress: new InlineProgress<ScriptWorkProgress>(progress => sink.Scripts(progress, "reempacotando"))))).ConfigureAwait(false);
        }
        catch (QaBlockedException blocked)
        {
            return RunQaBlocked(json, blocked, output);
        }

        if (json)
        {
            await output.WriteLineAsync(JsonEnvelope.Result(new
            {
                gameDirectory = result.GameDirectory,
                buildDirectory = result.BuildDirectory,
                archives = result.Archives.Select(a => new { path = a.GamePath, replacedFiles = a.ReplacedFiles }),
                scripts = result.RebuiltScripts.Count,
                reusedScripts = result.ReusedScripts,
                gameTitle = result.GameTitle,
                totalScripts = result.ScriptCount,
                lines = result.TotalLines,
                rebuilt = result.RebuiltScripts.Select(s => new { key = s.Key, name = s.Name, lines = s.Lines }),
                warnings = result.Warnings,
                encoding = EncodingData(result.Encoding),
                qa = QaData(result.Qa),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (cli.RichOutput)
        {
            cli.Ansi.Write(RunSummaries.BuildPanel(result, clock.Elapsed));
            WriteDetails(output, result);
            return ErrorHandler.OkExitCode;
        }

        foreach (var archive in result.Archives)
        {
            var path = Path.Combine(result.BuildDirectory, archive.GamePath.Replace('/', Path.DirectorySeparatorChar));
            var detail = archive.ReplacedFiles == 0
                ? "sem alterações, idêntico ao original"
                : $"{Number(archive.ReplacedFiles)} {Plural(archive.ReplacedFiles, "script alterado", "scripts alterados")}";
            await output.WriteLineAsync($"Gravado: {path} ({detail})");
        }

        await output.WriteLineAsync(Summary(result));
        WriteDetails(output, result);
        return ErrorHandler.OkExitCode;
    }

    /// <summary>O que vem depois do resumo: caracteres trocados, QA e avisos do build.</summary>
    private static void WriteDetails(TextWriter output, BuildResult result)
    {
        if (result.GameTitle is { } gameTitle)
        {
            output.WriteLine($"Título do jogo gravado: {gameTitle}");
        }

        if (result.Encoding.Replaced.Count > 0)
        {
            output.WriteLine($"Codificação '{result.Encoding.Strategy}': {Number(result.Encoding.Replaced.Count)} {Plural(result.Encoding.Replaced.Count, "caractere trocado", "caracteres trocados")}; vnf build --report lista cada um.");
        }

        foreach (var issue in result.Qa.BlockingIssues)
        {
            output.WriteLine(QaLine(issue));
        }

        if (result.Qa.UntranslatedCount > 0)
        {
            output.WriteLine(QaBlockedException.DescribeUntranslated(result.Qa));
        }

        if (result.Qa.Silenced.Count > 0)
        {
            output.WriteLine($"QA: {Number(result.Qa.Silenced.Count)} {Plural(result.Qa.Silenced.Count, "aviso marcado como ok", "avisos marcados como ok")}.");
        }

        foreach (var warning in result.Warnings.Take(MaxWarningsShown))
        {
            output.WriteLine($"Aviso: {warning}");
        }

        if (result.Warnings.Count > MaxWarningsShown)
        {
            output.WriteLine($"... e mais {Number(result.Warnings.Count - MaxWarningsShown)} aviso(s); use --json para ver todos.");
        }
    }

    internal static int RunQaBlocked(bool json, QaBlockedException blocked, TextWriter output)
    {
        var exitCode = ErrorHandler.QaBlockedExitCode;
        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(QaData(blocked.Report), exitCode));
            return exitCode;
        }

        var others = blocked.Report.BlockingIssues;
        foreach (var issue in others.Take(MaxQaIssuesShown))
        {
            output.WriteLine(QaLine(issue));
        }

        if (others.Count > MaxQaIssuesShown)
        {
            output.WriteLine($"... e mais {Number(others.Count - MaxQaIssuesShown)} ocorrência(s); use --json para ver todas.");
        }

        if (blocked.Report.UntranslatedCount > 0)
        {
            output.WriteLine(QaBlockedException.DescribeUntranslated(blocked.Report));
        }

        if (blocked.Report.Silenced.Count > 0)
        {
            output.WriteLine($"QA: {Number(blocked.Report.Silenced.Count)} {Plural(blocked.Report.Silenced.Count, "aviso marcado como ok", "avisos marcados como ok")}.");
        }

        output.WriteLine($"{QaBlockedException.DescribeBlocking(blocked.Report)}. O build não gravou nada; corrija, marque o aviso como ok na linha ou use --allow-warnings para liberar só os avisos.");
        return exitCode;
    }

    private static string QaLine(QaIssue issue) =>
        $"{QaBlockedException.Label(issue.Severity)} {issue.UnitId}  [{issue.RuleId}] {issue.Message}";

    private static object QaData(QaReport report) => new
    {
        errors = report.Errors.Count,
        warnings = report.BlockingIssues.Count(i => i.Severity == QaSeverity.Warning),
        untranslated = report.UntranslatedCount,
        silenced = report.Silenced.Count,
        issues = report.BlockingIssues.Select(i => new { rule = i.RuleId, severity = i.Severity == QaSeverity.Error ? "error" : "warn", unit = i.UnitId, script = i.ScriptKey, message = i.Message }),
    };

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
        if (scripts == 0 && result.ReusedScripts > 0)
        {
            return $"Nada mudou desde o build anterior: {Number(result.ReusedScripts)} {Plural(result.ReusedScripts, "script reaproveitado", "scripts reaproveitados")}, nenhum reconstruído.";
        }

        var reused = result.ReusedScripts > 0 ? $", {Number(result.ReusedScripts)} {Plural(result.ReusedScripts, "reaproveitado", "reaproveitados")}" : string.Empty;
        return $"Reconstruídos {Number(scripts)} {Plural(scripts, "script", "scripts")} ({Number(lines)} {Plural(lines, "linha trocada", "linhas trocadas")}{reused}); "
            + $"{Number(packages)} {Plural(packages, "pacote gravado", "pacotes gravados")} em build/";
    }

    private static string Number(int value) => value.ToString("N0", PortugueseBrazil);

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;
}
