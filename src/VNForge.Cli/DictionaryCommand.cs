using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Dictionaries;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf dict list|add|remove|enable|disable</c>: o pré-dicionário (troca no original antes da IA) e o pós-dicionário (troca na tradução, só quando a
/// condição bate), guardados no <c>dictionary</c> do <c>formatting.json</c>.
/// </summary>
internal static class DictionaryCommand
{
    private const string PreName = "pre";
    private const string PostName = "pos";

    public static Command Create(VnfCli cli)
    {
        var command = new Command("dict", "Gerencia o pré e o pós-dicionário do projeto (formatting.json).");
        command.Subcommands.Add(CreateList(cli));
        command.Subcommands.Add(CreateAdd(cli));
        command.Subcommands.Add(CreateRemove(cli));
        command.Subcommands.Add(CreateSwitch(cli, "enable", "Liga uma regra do dicionário.", on: true));
        command.Subcommands.Add(CreateSwitch(cli, "disable", "Desliga uma regra do dicionário sem apagá-la.", on: false));
        return command;
    }

    private static Argument<string> SideArgument() =>
        new("lado") { Description = "pre (troca no original, antes da IA) ou pos (troca na tradução, depois da IA)." };

    private static Command CreateList(VnfCli cli)
    {
        var command = new Command("list", "Lista as regras com o número de linhas que cada uma alcança.");
        command.SetAction(parseResult => List(cli, parseResult));
        return command;
    }

    private static Command CreateAdd(VnfCli cli)
    {
        var side = SideArgument();
        var from = new Argument<string>("de") { Description = "Texto a procurar." };
        var to = new Argument<string>("para") { Description = "Texto que entra no lugar (pode ser vazio)." };
        var regex = new Option<bool>("--regex") { Description = "Trata o texto a procurar como expressão regular do .NET ($1 e ${nome} valem no destino)." };
        var speaker = new Option<string?>("--speaker") { Description = "Só nas falas deste personagem (nome original ou traduzido).", HelpName = "nome" };
        var scene = new Option<string?>("--scene") { Description = "Só nesta cena (chave do script, nome, grupo ou pasta).", HelpName = "cena" };
        var contains = new Option<string?>("--contains") { Description = "Só nas linhas cujo original contém este trecho.", HelpName = "trecho" };

        var command = new Command("add", "Adiciona uma regra (por exemplo: vnf dict add pos você tu --speaker Ren).");
        command.Arguments.Add(side);
        command.Arguments.Add(from);
        command.Arguments.Add(to);
        command.Options.Add(regex);
        command.Options.Add(speaker);
        command.Options.Add(scene);
        command.Options.Add(contains);
        command.SetAction(parseResult =>
        {
            var conditions = new List<DictionaryCondition>();
            AddCondition(conditions, DictionaryConditionKind.Speaker, parseResult.GetValue(speaker));
            AddCondition(conditions, DictionaryConditionKind.Scene, parseResult.GetValue(scene));
            AddCondition(conditions, DictionaryConditionKind.SourceContains, parseResult.GetValue(contains));
            var rule = new DictionaryRule(parseResult.GetRequiredValue(from), parseResult.GetRequiredValue(to), parseResult.GetValue(regex)) { Conditions = conditions };
            return Add(cli, parseResult, parseResult.GetRequiredValue(side), rule);
        });
        return command;
    }

    private static Command CreateRemove(VnfCli cli)
    {
        var side = SideArgument();
        var number = new Argument<int>("numero") { Description = "Posição da regra na lista (veja vnf dict list)." };
        var command = new Command("remove", "Apaga uma regra do dicionário.");
        command.Arguments.Add(side);
        command.Arguments.Add(number);
        command.SetAction(parseResult => Change(cli, parseResult, parseResult.GetRequiredValue(side), parseResult.GetRequiredValue(number), "removed", _ => null));
        return command;
    }

    private static Command CreateSwitch(VnfCli cli, string name, string description, bool on)
    {
        var side = SideArgument();
        var number = new Argument<int>("numero") { Description = "Posição da regra na lista (veja vnf dict list)." };
        var command = new Command(name, description);
        command.Arguments.Add(side);
        command.Arguments.Add(number);
        command.SetAction(parseResult => Change(cli, parseResult, parseResult.GetRequiredValue(side), parseResult.GetRequiredValue(number), on ? "enabled" : "disabled", rule => rule with { Enabled = on }));
        return command;
    }

    private static void AddCondition(List<DictionaryCondition> conditions, DictionaryConditionKind kind, string? value)
    {
        if (value is not null)
        {
            conditions.Add(new DictionaryCondition(kind, value));
        }
    }

    private static int Add(VnfCli cli, ParseResult parseResult, string sideText, DictionaryRule rule)
    {
        var project = Open(cli, parseResult);
        var side = ParseSide(sideText);
        if (DictionaryStage.Validate(rule) is { } problem)
        {
            throw new UsageException($"A regra não serve: {problem}");
        }

        var profile = project.LoadFormatting();
        var rules = ListOf(profile.Dictionary, side).Append(rule).ToList();
        project.SaveFormatting(profile with { Dictionary = With(profile.Dictionary, side, rules) });

        Report(cli, parseResult, "added", side, rules.Count, rule);
        return ErrorHandler.OkExitCode;
    }

    private static int Change(VnfCli cli, ParseResult parseResult, string sideText, int number, string action, Func<DictionaryRule, DictionaryRule?> change)
    {
        var project = Open(cli, parseResult);
        var side = ParseSide(sideText);
        var profile = project.LoadFormatting();
        var rules = ListOf(profile.Dictionary, side).ToList();
        if (number < 1 || number > rules.Count)
        {
            throw new UsageException(
                $"Não existe a regra {number} no {SideTitle(side)}-dicionário.",
                rules.Count == 0 ? "A lista está vazia." : $"Use um número de 1 a {rules.Count} (vnf dict list).");
        }

        var rule = rules[number - 1];
        if (change(rule) is { } changed)
        {
            rules[number - 1] = changed;
        }
        else
        {
            rules.RemoveAt(number - 1);
        }

        project.SaveFormatting(profile with { Dictionary = With(profile.Dictionary, side, rules) });
        Report(cli, parseResult, action, side, number, rule);
        return ErrorHandler.OkExitCode;
    }

    private static void Report(VnfCli cli, ParseResult parseResult, string action, DictionarySide side, int number, DictionaryRule rule)
    {
        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { action, side = SideName(side), number, rule = Describe(rule) }));
            return;
        }

        var text = action switch
        {
            "added" => "Regra adicionada",
            "removed" => "Regra apagada",
            "enabled" => "Regra ligada",
            _ => "Regra desligada",
        };
        output.WriteLine($"{text} no {SideTitle(side)}-dicionário ({number}): {rule.Pattern} → {rule.With}{ConditionsText(rule, " · ")}");
    }

    private static int List(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = Open(cli, parseResult);
        var profile = project.LoadFormatting();
        var dictionary = profile.Dictionary;
        var texts = dictionary.IsEmpty ? [] : project.LoadAllText().Values.SelectMany(units => units).ToList();
        var context = dictionary.IsEmpty ? DictionaryContext.Empty : DictionaryContext.For(project);
        var rows = new List<(DictionarySide Side, int Number, DictionaryRule Rule, DictionaryImpact Impact)>();
        foreach (var side in new[] { DictionarySide.Pre, DictionarySide.Post })
        {
            var rules = ListOf(dictionary, side);
            for (var i = 0; i < rules.Count; i++)
            {
                rows.Add((side, i + 1, rules[i], DictionaryImpact.Of(rules[i], side, texts, context, profile.Kinds)));
            }
        }

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                rules = rows.Select(r => new { side = SideName(r.Side), number = r.Number, rule = Describe(r.Rule), applies = r.Impact.Applies, blocked = r.Impact.Blocked }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (rows.Count == 0)
        {
            cli.Ansi.MarkupLine("Nenhuma regra de dicionário. Use vnf dict add.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Lado"), CliViews.Column("Nº"), CliViews.Column("De"), CliViews.Column("Para"), CliViews.Column("Condições"), CliViews.Column("Linhas"));
        foreach (var (side, number, rule, impact) in rows)
        {
            var dim = rule.Enabled ? string.Empty : "dim";
            table.AddRow(
                new Markup(side == DictionarySide.Pre ? "Pré" : "Pós"),
                new Markup(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Cell(rule.Pattern + (rule.Regex ? " (regex)" : string.Empty), dim),
                Cell(rule.With, dim),
                Cell(rule.Conditions.Count == 0 ? "Sempre" : ConditionsText(rule, string.Empty).TrimStart(' ', '·'), dim),
                Cell(rule.Enabled ? LinesText(impact) : "desligada", "dim"));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static Markup Cell(string text, string style) =>
        new(style.Length == 0 ? CliViews.Escape(text) : $"[{style}]{CliViews.Escape(text)}[/]");

    private static string LinesText(DictionaryImpact impact) =>
        impact.Blocked == 0
            ? $"{impact.Applies}"
            : $"{impact.Applies} ({impact.Blocked} fora da condição)";

    private static string ConditionsText(DictionaryRule rule, string separator) =>
        rule.Conditions.Count == 0
            ? string.Empty
            : separator + string.Join(" e ", rule.Conditions.Select(c => c.Kind switch
            {
                DictionaryConditionKind.Speaker => $"falante {c.Value}",
                DictionaryConditionKind.Scene => $"cena {c.Value}",
                _ => $"original contém {c.Value}",
            }));

    private static object Describe(DictionaryRule rule) => new
    {
        pattern = rule.Pattern,
        with = rule.With,
        regex = rule.Regex,
        enabled = rule.Enabled,
        conditions = rule.Conditions.Select(c => new
        {
            kind = c.Kind switch { DictionaryConditionKind.Speaker => "speaker", DictionaryConditionKind.Scene => "scene", _ => "sourceContains" },
            value = c.Value,
        }),
    };

    private static DictionarySide ParseSide(string text) => text.Trim().ToLowerInvariant() switch
    {
        PreName => DictionarySide.Pre,
        PostName or "post" => DictionarySide.Post,
        _ => throw new UsageException($"'{text}' não é um lado do dicionário.", "Use pre (antes da IA) ou pos (depois da IA)."),
    };

    private static string SideName(DictionarySide side) => side == DictionarySide.Pre ? PreName : PostName;

    private static string SideTitle(DictionarySide side) => side == DictionarySide.Pre ? "pré" : "pós";

    private static IReadOnlyList<DictionaryRule> ListOf(DictionaryRules rules, DictionarySide side) => side == DictionarySide.Pre ? rules.Pre : rules.Post;

    private static DictionaryRules With(DictionaryRules rules, DictionarySide side, List<DictionaryRule> list) =>
        side == DictionarySide.Pre ? rules with { Pre = list } : rules with { Post = list };

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
