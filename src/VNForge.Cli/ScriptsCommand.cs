using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf scripts list|group|ungroup|rename-group</c>: os capítulos do projeto e o agrupamento manual das cenas em <c>scripts.json</c>.</summary>
internal static class ScriptsCommand
{
    private const int MaxKeysShown = 6;

    public static Command Create(VnfCli cli)
    {
        var command = new Command("scripts", "Mostra e muda o agrupamento das cenas em capítulos (scripts.json).");
        command.Subcommands.Add(CreateList(cli));
        command.Subcommands.Add(CreateGroup(cli));
        command.Subcommands.Add(CreateUngroup(cli));
        command.Subcommands.Add(CreateRenameGroup(cli));
        return command;
    }

    private static Command CreateList(VnfCli cli)
    {
        var command = new Command("list", "Lista os capítulos na ordem da história, com as cenas (chaves de script) de cada um.");
        command.SetAction(parseResult => List(cli, parseResult));
        return command;
    }

    private static Command CreateGroup(VnfCli cli)
    {
        var keys = KeysArgument();
        var name = new Argument<string>("nome") { Description = "Nome do capítulo, um que já existe ou um novo. \"Menus e nomes\" tira as cenas da história." };
        var command = new Command("group", "Põe as cenas num capítulo (por exemplo: vnf scripts group yst00010..yst00020 Prologo).");
        command.Arguments.Add(keys);
        command.Arguments.Add(name);
        command.SetAction(parseResult => Change(cli, parseResult, "grouped", project => ScriptGroupEditor.Group(project, [parseResult.GetRequiredValue(keys)], parseResult.GetRequiredValue(name))));
        return command;
    }

    private static Command CreateUngroup(VnfCli cli)
    {
        var keys = KeysArgument();
        var command = new Command("ungroup", "Devolve as cenas ao capítulo que a pasta do jogo propõe, desfazendo o que você escolheu.");
        command.Arguments.Add(keys);
        command.SetAction(parseResult => Change(cli, parseResult, "ungrouped", project => ScriptGroupEditor.Ungroup(project, [parseResult.GetRequiredValue(keys)])));
        return command;
    }

    private static Command CreateRenameGroup(VnfCli cli)
    {
        var group = new Argument<string>("capitulo") { Description = "Nome atual do capítulo ou o número dele na lista (vnf scripts list)." };
        var name = new Argument<string>("novo") { Description = "Novo nome. Um nome que já existe junta os dois capítulos." };
        var command = new Command("rename-group", "Dá outro nome a um capítulo (por exemplo: vnf scripts rename-group 2 Prologo).");
        command.Arguments.Add(group);
        command.Arguments.Add(name);
        command.SetAction(parseResult => Change(cli, parseResult, "renamed", project => ScriptGroupEditor.RenameGroup(project, parseResult.GetRequiredValue(group), parseResult.GetRequiredValue(name))));
        return command;
    }

    private static Argument<string> KeysArgument() => new("chaves")
    {
        Description = "Cenas: chaves separadas por vírgula (yst00012,yst00020) e intervalos na ordem numérica (yst00010..yst00020).",
    };

    private static int List(VnfCli cli, ParseResult parseResult)
    {
        var chapters = ChaptersOf(Open(cli, parseResult));
        if (parseResult.GetValue(cli.Options.Json))
        {
            parseResult.InvocationConfiguration.Output.WriteLine(JsonEnvelope.Result(new { chapters = chapters.Select(ChapterData) }));
            return ErrorHandler.OkExitCode;
        }

        if (chapters.Count == 0)
        {
            cli.Ansi.MarkupLine("O projeto não tem texto extraído. Rode `vnf extract` primeiro.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Nº", right: true), CliViews.Column("Capítulo"), CliViews.Column("Cenas", right: true), CliViews.Column("Chaves"));
        foreach (var chapter in chapters)
        {
            table.AddRow(
                new Markup(chapter.Number is { } number ? CliFormat.Number(number) : "[dim]·[/]"),
                new Markup(CliViews.Escape(chapter.Name)),
                new Markup(CliFormat.Number(chapter.ScriptKeys.Count)),
                new Markup(CliViews.Escape(KeysText(chapter.ScriptKeys))));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static int Change(VnfCli cli, ParseResult parseResult, string action, Func<ProjectFolder, ScriptGroupChange> edit)
    {
        ScriptGroupChange change;
        try
        {
            change = edit(Open(cli, parseResult));
        }
        catch (ScriptGroupingException ex)
        {
            throw new UsageException(ex.Message);
        }

        var output = parseResult.InvocationConfiguration.Output;
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { action, group = change.Group, scripts = change.Keys, chapters = change.Chapters.Select(ChapterData) }));
            return ErrorHandler.OkExitCode;
        }

        var scenes = CliFormat.Count(change.Keys.Count, "cena", "cenas");
        output.WriteLine(action switch
        {
            "grouped" => $"{scenes} em '{change.Group}'.",
            "renamed" => $"{scenes} do capítulo agora em '{change.Group}'.",
            _ => $"{scenes} de volta ao capítulo que a pasta propõe.",
        });
        output.WriteLine($"Capítulos: {string.Join(" · ", change.Chapters.Select(c => c.Name))}");
        return ErrorHandler.OkExitCode;
    }

    private static IReadOnlyList<ScriptGroupTotals> ChaptersOf(ProjectFolder project) =>
        ScriptGrouping.Build(project.LoadScriptNames(), ProjectStatusReporter.Create(project).Files);

    private static object ChapterData(ScriptGroupTotals chapter) => new
    {
        number = chapter.Number,
        name = chapter.Name,
        scripts = chapter.ScriptKeys,
        lines = chapter.Total,
        ready = chapter.Ready,
        toReview = chapter.ToReview,
        untranslated = chapter.Japanese,
    };

    private static string KeysText(IReadOnlyList<string> keys) =>
        keys.Count <= MaxKeysShown ? string.Join(", ", keys) : $"{string.Join(", ", keys.Take(MaxKeysShown))}… (+{keys.Count - MaxKeysShown})";

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
