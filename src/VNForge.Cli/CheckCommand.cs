using System.CommandLine;
using Spectre.Console;
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

    private static readonly string Accent = CliPalette.Hex(CliPalette.Accent);

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
        EnsureKnown(engine, rules);

        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var fixes = parseResult.GetValue(FixOption) ? ApplyFixes(project, engine, rules) : [];

        var report = engine.Run(QaContext.FromProject(project), new QaOptions(rules.Count == 0 ? null : rules, severity));
        var exitCode = report.HasErrors ? ErrorHandler.QaBlockedExitCode : ErrorHandler.OkExitCode;

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(Data(report, fixes), exitCode));
            return exitCode;
        }

        WriteText(cli.Ansi, report, fixes, rules.Contains(UntranslatedQaRule.RuleId));
        return exitCode;
    }

    private static void EnsureKnown(QaEngine engine, List<string> rules)
    {
        var unknown = rules.Where(id => engine.Find(id) is null).ToList();
        if (unknown.Count > 0)
        {
            throw new UsageException($"Regra de QA desconhecida: {string.Join(", ", unknown)}. Regras: {string.Join(", ", engine.Rules.Select(r => r.Id))}.");
        }
    }

    private static IReadOnlyList<QaFix> ApplyFixes(ProjectFolder project, QaEngine engine, List<string> rules)
    {
        // Projeto inválido tem de falhar antes de o --fix gravar qualquer coisa.
        _ = QaContext.FromProject(project);

        // Com --rules, corrige só as regras pedidas que sabem corrigir; as outras só entram na conferência.
        if (rules.Count == 0)
        {
            return QaFixer.Apply(project, engine).Fixes;
        }

        var fixable = rules.Where(id => engine.Find(id) is IQaFixer).ToList();
        return fixable.Count == 0 ? [] : QaFixer.Apply(project, engine, fixable).Fixes;
    }

    private static void WriteText(IAnsiConsole ansi, QaReport report, IReadOnlyList<QaFix> fixes, bool listUntranslated)
    {
        foreach (var fix in fixes)
        {
            ansi.MarkupLine($"[{CliPalette.Hex(CliPalette.Approved)}]CORRIGIDO[/] {CliViews.Escape(fix.UnitId)}  {RuleTag(fix.RuleId)}");
        }

        if (fixes.Count > 0)
        {
            ansi.MarkupLine($"{fixes.Count} {(fixes.Count == 1 ? "correção aplicada" : "correções aplicadas")} em text/*.json.");
        }

        foreach (var group in GroupByCategory(report, listUntranslated))
        {
            ansi.WriteLine();
            ansi.MarkupLine($"[bold {Accent}]{CliViews.Escape(group.Category)}[/] [dim]· {Describe(group.Errors, group.Warnings)}[/]");
            foreach (var line in group.Lines)
            {
                ansi.MarkupLine(line);
            }
        }

        ansi.WriteLine();
        ansi.MarkupLine($"[{CliPalette.Hex(SummaryColor(report))}]{QaBlockedException.Describe(report)}[/]");
        if (report.Silenced.Count > 0)
        {
            ansi.MarkupLine($"{report.Silenced.Count} {(report.Silenced.Count == 1 ? "aviso marcado como ok" : "avisos marcados como ok")}.");
        }
    }

    // Categorias com erro primeiro, depois na ordem do app; dentro de cada uma, erros antes de avisos.
    private static IEnumerable<CategoryGroup> GroupByCategory(QaReport report, bool listUntranslated)
    {
        var byCategory = report.Issues.GroupBy(i => QaCategories.Of(i.RuleId), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var ordered = QaCategories.All
            .Where(byCategory.ContainsKey)
            .OrderByDescending(category => byCategory[category].Any(i => i.Severity == QaSeverity.Error));

        foreach (var category in ordered)
        {
            var issues = byCategory[category].OrderByDescending(i => i.Severity).ToList();
            var lines = new List<string>();
            var untranslated = new List<QaIssue>();
            foreach (var issue in issues)
            {
                if (!listUntranslated && issue.RuleId == UntranslatedQaRule.RuleId)
                {
                    untranslated.Add(issue);
                    continue;
                }

                lines.Add($"  {Label(issue.Severity)} {CliViews.Escape(issue.UnitId)}  {CliViews.Escape(issue.Message)}  {RuleTag(issue.RuleId)}");
            }

            foreach (var script in untranslated.GroupBy(i => i.ScriptKey, StringComparer.Ordinal))
            {
                lines.Add($"  {Label(QaSeverity.Warning)} {CliViews.Escape(script.Key)}  {CliFormat.Count(script.Count(), "linha sem tradução", "linhas sem tradução")}  {RuleTag(UntranslatedQaRule.RuleId)}");
            }

            if (untranslated.Count > 0)
            {
                lines.Add($"  [dim]Para listar uma a uma: vnf check --rules {UntranslatedQaRule.RuleId}[/]");
            }

            yield return new CategoryGroup(category, issues.Count(i => i.Severity == QaSeverity.Error), issues.Count(i => i.Severity == QaSeverity.Warning), lines);
        }
    }

    private static Color SummaryColor(QaReport report)
    {
        if (report.HasErrors)
        {
            return CliPalette.Error;
        }

        return report.Warnings.Count > 0 ? CliPalette.Warning : CliPalette.Approved;
    }

    private static string Label(QaSeverity severity)
    {
        var color = severity == QaSeverity.Error ? CliPalette.Error : CliPalette.Warning;
        return $"[{CliPalette.Hex(color)}]{QaBlockedException.Label(severity)}[/]";
    }

    private static string RuleTag(string ruleId) => $"[dim]{CliViews.Escape($"[{ruleId}]")}[/]";

    private static string Describe(int errors, int warnings)
    {
        var parts = new List<string>();
        if (errors > 0)
        {
            parts.Add(CliFormat.Count(errors, "erro", "erros"));
        }

        if (warnings > 0)
        {
            parts.Add(CliFormat.Count(warnings, "aviso", "avisos"));
        }

        return string.Join(", ", parts);
    }

    private sealed record CategoryGroup(string Category, int Errors, int Warnings, IReadOnlyList<string> Lines);

    private static List<string> ParseRules(string? text) =>
        [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

    private static QaSeverity ParseSeverity(string? text) => text?.ToLowerInvariant() switch
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
