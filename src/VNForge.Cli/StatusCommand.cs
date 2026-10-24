using System.CommandLine;
using Spectre.Console;
using VNForge.Core.Glossary;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;

namespace VNForge.Cli;

/// <summary><c>vnf status</c>: progresso do projeto por status, por arquivo e por personagem, lido de <c>text/*.json</c>.</summary>
internal static class StatusCommand
{
    private const string NoSpeakerLabel = "(sem falante)";

    private const int MinBarWidth = 10;
    private const int MaxBarWidth = 60;
    private const int RowBarWidth = 20;

    public static Command Create(VnfCli cli)
    {
        var command = new Command("status", "Mostra o progresso da tradução: linhas por status, por arquivo e por personagem.");
        command.SetAction(parseResult => Run(cli, parseResult, parseResult.InvocationConfiguration.Output));
        return command;
    }

    private static int Run(VnfCli cli, ParseResult parseResult, TextWriter output)
    {
        var project = ProjectFolder.Open(Path.GetFullPath(parseResult.GetValue(cli.Options.Project) ?? ".", cli.CurrentDirectory));
        var status = ProjectStatusReporter.Create(project);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                total = Data(status.Total),
                files = status.Files.Select(f => Data(f.Counts, "key", f.Key)),
                characters = status.Characters.Select(c => Data(c.Counts, "speaker", c.Speaker)),
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteText(cli.Ansi, project, status);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteText(IAnsiConsole ansi, ProjectFolder project, ProjectStatus status)
    {
        var total = status.Total;
        var accent = CliPalette.Hex(CliPalette.Accent);
        var config = project.LoadConfig();
        ansi.MarkupLine($"[bold {accent}]{CliViews.Escape(config.Name)} · {CliViews.Escape(config.Languages.Target.ToUpperInvariant())}[/]");
        ansi.MarkupLine($"{CliFormat.Count(total.Total, "linha", "linhas")} · {CliFormat.Percent(total.Percent)} com tradução");
        ansi.Write(DistributionBar.Create(total, Math.Clamp(ansi.Profile.Width - 2, MinBarWidth, MaxBarWidth)));
        ansi.WriteLine();
        ansi.Write(DistributionBar.Legend(total));
        ansi.WriteLine();

        ansi.WriteLine();
        var scripts = CliViews.NewTable(CliViews.Column("Script"), CliViews.Column("Linhas", right: true), CliViews.Column("Progresso"), CliViews.Column("%", right: true));
        foreach (var file in status.Files)
        {
            scripts.AddRow(
                new Markup(CliViews.Escape(file.Key)),
                new Markup(CliFormat.Number(file.Counts.Total)),
                DistributionBar.Create(file.Counts, RowBarWidth),
                new Markup(CliFormat.Percent(file.Counts.Percent)));
        }

        ansi.Write(scripts);

        ansi.WriteLine();
        var names = CharacterNames(project);
        var characters = CliViews.NewTable(CliViews.Column("Personagem"), CliViews.Column("Linhas", right: true), CliViews.Column("Progresso"), CliViews.Column("%", right: true));
        foreach (var character in status.Characters)
        {
            characters.AddRow(
                new Markup(SpeakerLabel(character.Speaker, names)),
                new Markup(CliFormat.Number(character.Counts.Total)),
                DistributionBar.Create(character.Counts, RowBarWidth),
                new Markup(CliFormat.Percent(character.Counts.Percent)));
        }

        ansi.Write(characters);
    }

    // characters.json inválido não derruba o status: sem ele os falantes aparecem sem tradução do nome.
    private static Dictionary<string, string> CharacterNames(ProjectFolder project)
    {
        try
        {
            return project.LoadCharacters()
                .GroupBy(c => c.Source.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal);
        }
        catch (GlossaryFileException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string SpeakerLabel(string? speaker, Dictionary<string, string> names)
    {
        if (speaker is null)
        {
            return $"[dim]{NoSpeakerLabel}[/]";
        }

        return names.TryGetValue(speaker.Trim(), out var target)
            ? $"{CliViews.Escape(speaker)} → {CliViews.Escape(target)}"
            : CliViews.Escape(speaker);
    }

    private static Dictionary<string, object?> Data(StatusCounts counts, string? keyName = null, string? keyValue = null)
    {
        var data = new Dictionary<string, object?>();
        if (keyName is not null)
        {
            data[keyName] = keyValue;
        }

        data["lines"] = counts.Total;
        data["untranslated"] = counts.Untranslated;
        data["machine"] = counts.Machine;
        data["translated"] = counts.Translated;
        data["reviewed"] = counts.Reviewed;
        data["approved"] = counts.Approved;
        data["percent"] = counts.Percent;
        return data;
    }
}
