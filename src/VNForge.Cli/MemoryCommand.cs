using System.CommandLine;
using System.Globalization;
using Spectre.Console;
using VNForge.Core.Logging;
using VNForge.Core.Memory;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary>
/// <c>vnf memory sync|find|stats</c>: a memória de tradução entre projetos (#537), um arquivo do usuário em <c>translation-memory.json</c>.
/// O glossário pessoal, no mesmo lugar, é o <c>vnf glossary personal</c> (<see cref="PersonalGlossaryCommand"/>).
/// </summary>
internal static class MemoryCommand
{
    public static Command Create(VnfCli cli)
    {
        var command = new Command("memory", "Memória de tradução entre projetos: os pares aprovados de todos os seus projetos.");
        command.Subcommands.Add(CreateSync(cli));
        command.Subcommands.Add(CreateFind(cli));
        command.Subcommands.Add(CreateStats(cli));
        return command;
    }

    private static Command CreateSync(VnfCli cli)
    {
        var command = new Command("sync", "Guarda na memória os pares aprovados deste projeto e tira os que deixaram de estar aprovados.");
        command.SetAction(parseResult => Sync(cli, parseResult));
        return command;
    }

    private static Command CreateFind(VnfCli cli)
    {
        var text = new Argument<string>("original") { Description = "Texto no idioma original, como aparece no texto extraído." };
        var minimum = new Option<int>("--min") { Description = "Semelhança mínima, de 1 a 100, da correspondência aproximada.", HelpName = "percentual", DefaultValueFactory = _ => 80 };
        var limit = new Option<int>("--limit") { Description = "Quantas correspondências mostrar.", HelpName = "n", DefaultValueFactory = _ => TranslationMemory.DefaultLimit };
        var command = new Command("find", "Procura o original na memória, nos outros projetos: a correspondência exata (100%) e as aproximadas.");
        command.Arguments.Add(text);
        command.Options.Add(minimum);
        command.Options.Add(limit);
        command.SetAction(parseResult => Find(cli, parseResult, parseResult.GetRequiredValue(text), parseResult.GetValue(minimum), parseResult.GetValue(limit)));
        return command;
    }

    private static Command CreateStats(VnfCli cli)
    {
        var command = new Command("stats", "Mostra quantos pares a memória tem e de quantos projetos.");
        command.SetAction(parseResult => Stats(cli, parseResult));
        return command;
    }

    private static int Sync(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var project = OpenProject(cli, parseResult);
        var result = new TranslationMemory(cli.UserConfigDirectory).SyncProject(project, TimeProvider.System.GetUtcNow());
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { added = result.Added, updated = result.Updated, removed = result.Removed, projectTotal = result.ProjectTotal }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(result.Changed
            ? $"Memória atualizada: {result.Added} novos, {result.Updated} trocados, {result.Removed} removidos. Este projeto tem {result.ProjectTotal} pares aprovados."
            : $"A memória já estava em dia. Este projeto tem {result.ProjectTotal} pares aprovados.");
        return ErrorHandler.OkExitCode;
    }

    private static int Find(VnfCli cli, ParseResult parseResult, string source, int minimumPercent, int limit)
    {
        if (minimumPercent is < 1 or > 100)
        {
            throw new UsageException("A semelhança mínima vai de 1 a 100.");
        }

        if (limit < 1)
        {
            throw new UsageException("O limite precisa ser 1 ou mais.");
        }

        var output = parseResult.InvocationConfiguration.Output;
        var project = OpenProject(cli, parseResult);
        var matches = new TranslationMemory(cli.UserConfigDirectory)
            .Find(source, TranslationMemory.ProjectKeyOf(project), minimumPercent / 100.0, limit);
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                matches = matches.Select(m => new
                {
                    percent = m.Percent,
                    exact = m.IsExact,
                    source = m.Entry.Source,
                    target = m.Entry.Target,
                    project = m.Entry.ProjectName,
                    approvedAt = m.Entry.ApprovedAt,
                }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (matches.Count == 0)
        {
            cli.Ansi.MarkupLine("Nenhum par parecido nos outros projetos.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("%", right: true), CliViews.Column("Tradução"), CliViews.Column("Projeto"), CliViews.Column("Aprovada em"));
        foreach (var match in matches)
        {
            table.AddRow(
                new Markup(match.Percent.ToString(CultureInfo.InvariantCulture)),
                new Markup(CliViews.Escape(match.Entry.Target)),
                new Markup(CliViews.Escape(match.Entry.ProjectName)),
                new Markup($"[dim]{match.Entry.ApprovedAt.ToLocalTime():dd/MM/yyyy}[/]"));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static int Stats(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var stats = new TranslationMemory(cli.UserConfigDirectory).Stats();
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new { entries = stats.Entries, projects = stats.Projects }));
            return ErrorHandler.OkExitCode;
        }

        output.WriteLine(stats.Entries == 0
            ? "A memória está vazia. Rode 'vnf memory sync' em um projeto com linhas aprovadas."
            : $"{stats.Entries} pares aprovados em {stats.Projects} projetos.");
        return ErrorHandler.OkExitCode;
    }

    internal static ProjectFolder OpenProject(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
