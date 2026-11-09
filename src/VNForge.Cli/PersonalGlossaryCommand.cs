using System.CommandLine;
using System.Globalization;
using Spectre.Console;
using VNForge.Core.Logging;
using VNForge.Core.Memory;
using VNForge.Core.Output;

namespace VNForge.Cli;

/// <summary><c>vnf glossary personal list|keep|use|remove</c>: o glossário pessoal (#537), termos que valem em toda VN e entram em projeto novo.</summary>
internal static class PersonalGlossaryCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("personal", "Glossário pessoal: termos que valem em toda VN, guardados fora dos projetos.");
        command.Subcommands.Add(CreateList(cli));
        command.Subcommands.Add(CreateKeep(cli));
        command.Subcommands.Add(CreateUse(cli));
        command.Subcommands.Add(CreateRemove(cli));
        return command;
    }

    private static Argument<string> SourceArgument() => new("original") { Description = "Termo no idioma original." };

    private static Command CreateList(VnfCli cli)
    {
        var command = new Command("list", "Lista os termos do glossário pessoal e como cada um está neste projeto.");
        command.SetAction(parseResult => List(cli, parseResult));
        return command;
    }

    private static Command CreateKeep(VnfCli cli)
    {
        var source = SourceArgument();
        var command = new Command("keep", "Guarda no glossário pessoal um termo do glossário deste projeto.");
        command.Arguments.Add(source);
        command.SetAction(parseResult => Keep(cli, parseResult, parseResult.GetRequiredValue(source)));
        return command;
    }

    private static Command CreateUse(VnfCli cli)
    {
        var source = SourceArgument();
        var replace = new Option<bool>("--replace") { Description = "Troca a tradução do projeto se ela for diferente da pessoal." };
        var command = new Command("use", "Copia um termo do glossário pessoal para o glossário deste projeto.");
        command.Arguments.Add(source);
        command.Options.Add(replace);
        command.SetAction(parseResult => Use(cli, parseResult, parseResult.GetRequiredValue(source), parseResult.GetValue(replace)));
        return command;
    }

    private static Command CreateRemove(VnfCli cli)
    {
        var source = SourceArgument();
        var command = new Command("remove", "Tira um termo do glossário pessoal. O glossário dos projetos não muda.");
        command.Arguments.Add(source);
        command.SetAction(parseResult => Remove(cli, parseResult, parseResult.GetRequiredValue(source)));
        return command;
    }

    private static string StateName(PersonalTermState state) => state switch
    {
        PersonalTermState.InProject => "no-projeto",
        PersonalTermState.Differs => "difere",
        _ => "fora-do-projeto",
    };

    private static string StateText(PersonalTermState state) => state switch
    {
        PersonalTermState.InProject => "[green]no projeto[/]",
        PersonalTermState.Differs => "[yellow]difere do projeto[/]",
        _ => "[dim]fora do projeto[/]",
    };

    private static int List(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = MemoryCommand.OpenProject(cli, parseResult);
        var inProject = project.LoadGlossary();
        var terms = new PersonalGlossary(cli.UserConfigDirectory).Load()
            .Select(t => (Term: t, State: PersonalGlossary.StateIn(t, inProject)))
            .ToList();
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                terms = terms.Select(t => new { source = t.Term.Source, target = t.Term.Target, notes = t.Term.Notes, projects = t.Term.ProjectCount, seed = t.Term.Seed, state = StateName(t.State) }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (terms.Count == 0)
        {
            cli.Ansi.MarkupLine("O glossário pessoal está vazio. Use 'vnf glossary personal keep' para guardar um termo deste projeto.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(
            CliViews.Column("Original"), CliViews.Column("Tradução"), CliViews.Column("Notas"), CliViews.Column("Projetos", right: true), CliViews.Column("Neste projeto"));
        foreach (var (term, state) in terms)
        {
            table.AddRow(
                new Markup(CliViews.Escape(term.Source)),
                new Markup(CliViews.Escape(term.Target)),
                new Markup($"[dim]{CliViews.Escape(term.Notes ?? string.Empty)}[/]"),
                new Markup(term.ProjectCount.ToString(CultureInfo.InvariantCulture)),
                new Markup(StateText(state)));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static int Keep(VnfCli cli, ParseResult parseResult, string source)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = MemoryCommand.OpenProject(cli, parseResult);
        var original = source.Trim();
        var term = project.LoadGlossary().FirstOrDefault(t => t.Source.Trim() == original)
            ?? throw new UsageException($"'{original}' não está no glossário deste projeto. Adicione com 'vnf glossary add'.");
        var kept = new PersonalGlossary(cli.UserConfigDirectory).Keep(project, term);
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { action = "kept", source = kept.Source, target = kept.Target, projects = kept.ProjectCount }));
        }
        else
        {
            output.WriteLine($"Guardado no glossário pessoal: {kept.Source} → {kept.Target} ({kept.ProjectCount} projetos)");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int Use(VnfCli cli, ParseResult parseResult, string source, bool replace)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = MemoryCommand.OpenProject(cli, parseResult);
        PersonalTermState state;
        try
        {
            state = new PersonalGlossary(cli.UserConfigDirectory).UseInProject(project, source, replace);
        }
        catch (KeyNotFoundException e)
        {
            throw new UsageException(e.Message);
        }

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { source = source.Trim(), state = StateName(state) }));
        }
        else
        {
            output.WriteLine(state == PersonalTermState.Differs
                ? $"O projeto já traduz '{source.Trim()}' de outro jeito e ficou como estava. Use --replace para trocar."
                : $"Termo no glossário do projeto: {source.Trim()}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int Remove(VnfCli cli, ParseResult parseResult, string source)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var removed = new PersonalGlossary(cli.UserConfigDirectory).Remove(source);
        if (!removed)
        {
            throw new UsageException($"'{source.Trim()}' não está no glossário pessoal.");
        }

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { action = "removed", source = source.Trim() }));
        }
        else
        {
            output.WriteLine($"Termo tirado do glossário pessoal: {source.Trim()}");
        }

        return ErrorHandler.OkExitCode;
    }
}
