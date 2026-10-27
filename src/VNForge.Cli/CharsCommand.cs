using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Glossary;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf chars list|set</c>: os personagens de <c>characters.json</c>.</summary>
internal static class CharsCommand
{
    private static readonly Dictionary<string, CharacterGender> GendersByName =
        Enum.GetValues<CharacterGender>().ToDictionary(GenderName, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] GenderNames = [.. Enum.GetValues<CharacterGender>().Select(GenderName)];

    public static Command Create(VnfCli cli)
    {
        var command = new Command("chars", "Gerencia os personagens do projeto (characters.json).");
        command.Subcommands.Add(CreateList(cli));
        command.Subcommands.Add(CreateSet(cli));
        return command;
    }

    private static Command CreateList(VnfCli cli)
    {
        var command = new Command("list", "Lista os personagens com tradução do nome, gênero e tom de fala.");
        command.SetAction(parseResult => List(cli, parseResult));
        return command;
    }

    private static Command CreateSet(VnfCli cli)
    {
        var source = new Argument<string>("original") { Description = "Nome original, exatamente como o falante extraído." };
        var target = new Argument<string>("traducao") { Description = "Nome traduzido." };
        var gender = new Option<string?>("--gender")
        {
            Description = $"Gênero para concordância: {string.Join(", ", GenderNames)}.",
            HelpName = "valor",
        };
        var tone = new Option<string?>("--tone") { Description = "Tom de fala em texto livre (por exemplo: formal, usa honoríficos).", HelpName = "texto" };

        var command = new Command("set", "Cria ou atualiza um personagem; o que não for informado fica como está.");
        command.Arguments.Add(source);
        command.Arguments.Add(target);
        command.Options.Add(gender);
        command.Options.Add(tone);
        command.SetAction(parseResult => Set(cli, parseResult, parseResult.GetRequiredValue(source), parseResult.GetRequiredValue(target), parseResult.GetValue(gender), parseResult.GetValue(tone)));
        return command;
    }

    private static int Set(VnfCli cli, ParseResult parseResult, string source, string target, string? genderText, string? tone)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var original = source.Trim();
        if (original.Length == 0)
        {
            throw new UsageException("O nome original não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new UsageException("A tradução do nome não pode ser vazia.");
        }

        var gender = genderText is null ? (CharacterGender?)null : ParseGender(genderText);
        var project = Open(cli, parseResult);
        var characters = project.LoadCharacters().ToList();
        var index = characters.FindIndex(c => CharacterBook.NameComparer.Equals(c.Source, original));

        var current = index >= 0 ? characters[index] : null;
        var entry = new CharacterEntry(original, target, gender ?? current?.Gender ?? CharacterGender.Unknown, tone ?? current?.Tone);
        if (current is null)
        {
            characters.Add(entry);
        }
        else
        {
            characters[index] = entry;
        }

        project.SaveCharacters(characters);
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                action = current is null ? "added" : "updated",
                source = entry.Source,
                target = entry.Target,
                gender = GenderName(entry.Gender),
                tone = entry.Tone,
            }));
        }
        else
        {
            output.WriteLine($"{(current is null ? "Personagem adicionado" : "Personagem atualizado")}: {entry.Source} → {entry.Target}");
        }

        return ErrorHandler.OkExitCode;
    }

    private static int List(VnfCli cli, ParseResult parseResult)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var characters = Open(cli, parseResult).LoadCharacters();
        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                characters = characters.Select(c => new { source = c.Source, target = c.Target, gender = GenderName(c.Gender), tone = c.Tone }),
            }));
            return ErrorHandler.OkExitCode;
        }

        if (characters.Count == 0)
        {
            cli.Ansi.MarkupLine("Nenhum personagem cadastrado.");
            return ErrorHandler.OkExitCode;
        }

        var table = CliViews.NewTable(CliViews.Column("Original"), CliViews.Column("Tradução"), CliViews.Column("Gênero"), CliViews.Column("Tom"));
        foreach (var c in characters)
        {
            table.AddRow(
                new Markup(CliViews.Escape(c.Source)),
                new Markup(CliViews.Escape(c.Target)),
                new Markup(GenderName(c.Gender)),
                new Markup($"[dim]{CliViews.Escape(c.Tone ?? string.Empty)}[/]"));
        }

        cli.Ansi.Write(table);
        return ErrorHandler.OkExitCode;
    }

    private static CharacterGender ParseGender(string text) =>
        GendersByName.TryGetValue(text.Trim(), out var gender)
            ? gender
            : throw new UsageException($"Gênero desconhecido: '{text}'. Use um de: {string.Join(", ", GenderNames)}.");

    private static string GenderName(CharacterGender gender) => gender.ToString().ToLowerInvariant();

    private static ProjectFolder Open(VnfCli cli, ParseResult parseResult) =>
        ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
}
