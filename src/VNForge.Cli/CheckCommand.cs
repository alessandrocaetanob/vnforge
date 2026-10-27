using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Formatting;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

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
        var fixes = parseResult.GetValue(FixOption) ? ApplyFixes(project, engine, rules) : NoFixes;

        var report = engine.Run(QaContext.FromProject(project), new QaOptions(rules.Count == 0 ? null : rules, severity));
        var exitCode = report.HasErrors ? ErrorHandler.QaBlockedExitCode : ErrorHandler.OkExitCode;
        var settings = SettingGroup.Build(project, report);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(Data(report, fixes, settings), exitCode));
            return exitCode;
        }

        WriteText(cli.Ansi, report, fixes, settings, rules.Contains(UntranslatedQaRule.RuleId));
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

    private static readonly QaFixResult NoFixes = new([], new Dictionary<string, IReadOnlyList<TextUnit>>());

    private static QaFixResult ApplyFixes(ProjectFolder project, QaEngine engine, List<string> rules)
    {
        // Projeto inválido tem de falhar antes de o --fix gravar qualquer coisa.
        _ = QaContext.FromProject(project);

        // Com --rules, corrige só as regras pedidas que sabem corrigir; as outras só entram na conferência.
        if (rules.Count == 0)
        {
            return QaFixer.Apply(project, engine);
        }

        var fixable = rules.Where(id => engine.Find(id) is IQaFixer).ToList();
        return fixable.Count == 0 ? NoFixes : QaFixer.Apply(project, engine, fixable);
    }

    private static void WriteText(IAnsiConsole ansi, QaReport report, QaFixResult result, IReadOnlyList<SettingGroup> settings, bool listUntranslated)
    {
        var fixes = result.Fixes;
        foreach (var setting in result.SettingsFixed)
        {
            ansi.MarkupLine($"[{CliPalette.Hex(CliPalette.Approved)}]CORRIGIDO[/] {CliViews.Escape($"formatting.json: {setting.Setting} passou de {setting.CurrentLabel} para {setting.SafeLabel}.")}");
        }

        foreach (var fix in fixes)
        {
            ansi.MarkupLine($"[{CliPalette.Hex(CliPalette.Approved)}]CORRIGIDO[/] {CliViews.Escape(fix.UnitId)}  {RuleTag(fix.RuleId)}");
        }

        if (fixes.Count > 0)
        {
            ansi.MarkupLine($"{fixes.Count} {(fixes.Count == 1 ? "correção aplicada" : "correções aplicadas")} em text/*.json.");
        }

        foreach (var group in GroupByCategory(report, settings, listUntranslated))
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
    private static IEnumerable<CategoryGroup> GroupByCategory(QaReport report, IReadOnlyList<SettingGroup> settings, bool listUntranslated)
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
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var issue in issues)
            {
                if (settings.FirstOrDefault(g => g.Owns(issue)) is { } setting)
                {
                    if (written.Add(setting.Conflict.Field))
                    {
                        lines.Add($"  {Label(issue.Severity)} {CliViews.Escape(setting.Describe())}  {RuleTag(issue.RuleId)}");
                    }

                    continue;
                }

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

    // O QaIssue não carrega o campo do ajuste; a mensagem da regra começa sempre por "O ajuste {Setting} ({CurrentLabel}) gera '{caractere}'".
    private sealed record SettingGroup(FormattingConflict Conflict, string Strategy, IReadOnlyList<QaIssue> Issues)
    {
        public int Errors => Issues.Count;

        public int Lines => Issues.Select(i => i.UnitId).Distinct(StringComparer.Ordinal).Count();

        public string Characters
        {
            get
            {
                var found = Issues.Select(i => Created(i.Message)).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                return found.Count == 0 ? Conflict.Characters : string.Concat(found);
            }
        }

        public bool Owns(QaIssue issue) =>
            issue.RuleId == FormattingConflictRule.RuleId && issue.Message.StartsWith(Prefix(Conflict), StringComparison.Ordinal);

        public string Describe()
        {
            var count = Errors == Lines
                ? CliFormat.Count(Lines, "linha com erro", "linhas com erro")
                : $"{CliFormat.Count(Errors, "erro", "erros")} em {CliFormat.Count(Lines, "linha", "linhas")}";
            var action = Conflict.Safe is null ? "desliga o ajuste" : $"troca para {Conflict.SafeLabel}";
            return $"{Conflict.Setting} ({Conflict.CurrentLabel}): {count}. A estratégia {Strategy} não grava '{Characters}'. vnf check --fix {action}.";
        }

        public static IReadOnlyList<SettingGroup> Build(ProjectFolder project, QaReport report)
        {
            if (!report.Issues.Any(i => i.RuleId == FormattingConflictRule.RuleId))
            {
                return [];
            }

            var strategy = project.LoadConfig().Encoding.Strategy;
            return
            [
                .. QaFixer.FindSettingConflicts(project)
                    .Select(conflict => new SettingGroup(conflict, strategy, [.. report.Issues.Where(i => i.RuleId == FormattingConflictRule.RuleId && i.Message.StartsWith(Prefix(conflict), StringComparison.Ordinal))]))
                    .Where(group => group.Errors > 0),
            ];
        }

        private static string Prefix(FormattingConflict conflict) => $"O ajuste {conflict.Setting} ({conflict.CurrentLabel}) gera '";

        private static string Created(string message)
        {
            var start = message.IndexOf("gera '", StringComparison.Ordinal);
            var end = start < 0 ? -1 : message.IndexOf("', que ", start, StringComparison.Ordinal);
            return start < 0 || end < 0 ? string.Empty : message[(start + 6)..end];
        }
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

    private static object Data(QaReport report, QaFixResult result, IReadOnlyList<SettingGroup> settings) => new
    {
        errors = report.Errors.Count,
        warnings = report.Warnings.Count,
        silenced = report.Silenced.Count,
        @fixed = result.Fixes.Count,
        fixes = result.Fixes.Select(f => new { rule = f.RuleId, unit = f.UnitId, script = f.ScriptKey }),
        settingsFixed = result.SettingsFixed.Select(c => new { field = c.Field, setting = c.Setting, before = c.Current, after = c.Safe, beforeLabel = c.CurrentLabel, afterLabel = c.SafeLabel }),
        settingConflicts = settings.Select(g => new
        {
            field = g.Conflict.Field,
            setting = g.Conflict.Setting,
            current = g.Conflict.Current,
            currentLabel = g.Conflict.CurrentLabel,
            safe = g.Conflict.Safe,
            safeLabel = g.Conflict.SafeLabel,
            characters = g.Characters,
            errors = g.Errors,
            lines = g.Lines,
        }),
        issues = report.Issues.Select(i => new { rule = i.RuleId, severity = i.Severity == QaSeverity.Error ? "error" : "warn", unit = i.UnitId, script = i.ScriptKey, message = i.Message }),
    };
}
