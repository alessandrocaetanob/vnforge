using System.CommandLine;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.Cli;

/// <summary><c>vnf check</c>: roda o QA sobre <c>text/*.json</c> e lista os problemas; sai com <c>2</c> quando há erro. Com <c>--fix</c>, corrige antes o que é seguro.</summary>
internal static class CheckCommand
{
    private static readonly Option<string?> RulesOption = new("--rules")
    {
        Description = "Só estas regras, pelo id (espaco-duplo,marcador-diferente); aceita vários, separados por vírgula.",
        HelpName = "lista",
    };

    private static readonly Option<string?> SeverityOption = new("--severity")
    {
        Description = "Filtro de gravidade: error (só erros) ou warn (erros e avisos, o padrão).",
        HelpName = "nível",
    };

    private static readonly Option<bool> FixOption = new("--fix")
    {
        Description = "Corrige o que é seguro corrigir (espaços duplos etc.), grava em text/*.json e confere de novo.",
    };

    public static Command Create(VnfCli cli)
    {
        var command = new Command("check", "Roda o QA nas traduções e lista os problemas; sai com 2 se houver erro.");
        command.Options.Add(RulesOption);
        command.Options.Add(SeverityOption);
        command.Options.Add(FixOption);
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.InvocationConfiguration.Output));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output)
    {
        var rules = ParseRules(parseResult.GetValue(RulesOption));
        var severity = ParseSeverity(parseResult.GetValue(SeverityOption));
        var engine = cli.Qa;
        var unknown = rules.Where(id => engine.Find(id) is null).ToList();
        if (unknown.Count > 0)
        {
            throw new UsageException($"Regra de QA desconhecida: {string.Join(", ", unknown)}. Regras: {string.Join(", ", engine.Rules.Select(r => r.Id))}.");
        }

        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));

        IReadOnlyList<QaFix> fixes = [];
        if (parseResult.GetValue(FixOption))
        {
            // Com --rules, corrige só as regras pedidas que sabem corrigir; as outras só entram na conferência.
            var fixable = rules.Where(id => engine.Find(id) is IQaFixer).ToList();
            if (rules.Count == 0 || fixable.Count > 0)
            {
                fixes = QaFixer.Apply(project, engine, rules.Count == 0 ? null : fixable).Fixes;
            }
        }

        var report = engine.Run(QaContext.FromProject(project), new QaOptions(rules.Count == 0 ? null : rules, severity));
        var exitCode = report.HasErrors ? ErrorHandler.QaBlockedExitCode : ErrorHandler.OkExitCode;

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(Data(report, fixes), exitCode));
            return exitCode;
        }

        foreach (var fix in fixes)
        {
            output.WriteLine($"CORRIGIDO {fix.UnitId}  [{fix.RuleId}]");
        }

        if (fixes.Count > 0)
        {
            output.WriteLine($"{fixes.Count} {(fixes.Count == 1 ? "correção aplicada" : "correções aplicadas")} em text/*.json.");
        }

        foreach (var issue in report.Issues)
        {
            output.WriteLine($"{QaBlockedException.Label(issue.Severity)} {issue.UnitId}  {issue.Message}");
        }

        output.WriteLine(QaBlockedException.Describe(report));
        if (report.Silenced.Count > 0)
        {
            output.WriteLine($"{report.Silenced.Count} {(report.Silenced.Count == 1 ? "aviso marcado como ok" : "avisos marcados como ok")}.");
        }

        return exitCode;
    }

    private static List<string> ParseRules(string? text) =>
        [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

    private static QaSeverity ParseSeverity(string? text) => text switch
    {
        null => QaSeverity.Warning,
        "warn" => QaSeverity.Warning,
        "error" => QaSeverity.Error,
        _ => throw new UsageException($"--severity aceita error ou warn (recebeu '{text}')."),
    };

    private static object Data(QaReport report, IReadOnlyList<QaFix> fixes) => new
    {
        errors = report.Errors.Count,
        warnings = report.Warnings.Count,
        silenced = report.Silenced.Count,
        @fixed = fixes.Count,
        fixes = fixes.Select(f => new { rule = f.RuleId, unit = f.UnitId, script = f.ScriptKey }),
        issues = report.Issues.Select(i => new { rule = i.RuleId, severity = i.Severity == QaSeverity.Error ? "error" : "warn", unit = i.UnitId, script = i.ScriptKey, message = i.Message }),
    };
}
