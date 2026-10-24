using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Glossary;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf glossary add|list</c>: os termos fixos de <c>glossary.json</c>.</summary>
internal static class GlossaryCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("glossary", "Gerencia o glossário do projeto (glossary.json).");
        command.Subcommands.Add(CreateAdd(cli));
        command.Subcommands.Add(CreateList(cli));
        return command;
    }

    private static Command CreateAdd(VnfCli cli)
    {
        var source = new Argument<string>("original") { Description = "Termo no idioma original, como aparece no texto extraído." };
        var target = new Argument<string>("traducao") { Description = "Tradução fixa do termo." };
        var notes = new Option<string?>("--notes") { Description = "Observação para o tradutor e para a IA.", HelpName = "texto" };
        var force = new Option<bool>("--force") { Description = "Substitui o termo se ele já existir." };

        var command = new Command("add", "Adiciona um termo ao glossário (por exemplo: vnf glossary add \"先輩\" \"veterano\").");
        command.Arguments.Add(source);
        command.Arguments.Add(target);
        command.Options.Add(notes);
        command.Options.Add(force);
        command.SetAction(parseResult => Add(cli, parseResult, parseResult.GetRequiredValue(source), parseResult.GetRequiredValue(target), parseResult.GetValue(notes), parseResult.GetValue(force)));
        return command;
    }

    private static Command CreateList(VnfCli cli)
    {
        var command = new Command("list", "Lista os termos do glossário.");
        command.SetAction(parseResult => List(cli, parseResult));
        return command;
    }

    private static int Add(VnfCli cli, ParseResult parseResult, string source, string target, string? notes, bool force)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = Open(cli, parseResult);
        var original = source.Trim();
        if (original.Length == 0)
        {
            throw new UsageException("O termo original não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new UsageException("A tradução do termo não pode ser vazia.");
        }

        var terms = project.LoadGlossary().ToList();
        var index = terms.FindIndex(t => string.Equals(t.Source.Trim(), original, StringComparison.Ordinal));
        if (index >= 0 && !force)
        {
            throw new UsageException($"'{original}' já está no glossário como '{terms[index].Target}'. Use --force para substituir.");
        }

        var term = new GlossaryTerm(original, target, notes);
        if (index >= 0)
        {
            terms[index] = term;
        }
        else
        {
            terms.Add(term);
        }

        project.SaveGlossary(terms);
        var action = index >= 0 ? "updated" : "added";
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { action, source = term.Source, target = term.Target, notes = term.Notes }));
        }
        else
        {
            output.WriteLine($"{(index >= 0 ? "Termo atualizado" : "Termo adicionado")}: {term.Source} → {term.Target}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int List(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var terms = Open(cli, parseResult).LoadGlossary();
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { terms = terms.Select(t => new { source = t.Source, target = t.Target, notes = t.Notes }) }));
            return ErrorHandler.OkExitCode;
        }

        if (terms.Count == 0)
        {
            cli.Ansi.MarkupLine("O glossário está vazio.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Original"), CliViews.Column("Tradução"), CliViews.Column("Notas"));
        foreach (var term in terms)
        {
            table.AddRow(
                new Markup(CliViews.Escape(term.Source)),
                new Markup(CliViews.Escape(term.Target)),
                new Markup($"[dim]{CliViews.Escape(term.Notes ?? string.Empty)}[/]"));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
