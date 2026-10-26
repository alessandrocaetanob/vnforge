using System.CommandLine;
using Spectre.Console;
using Spectre.Console.Rendering;
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
        var scriptNames = ScriptNamesOf(project);

        if (parseResult.GetValue(cli.Options.Json))
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                total = Data(status.Total),
                files = status.Files.Select(f => Data(f.Counts, "key", f.Key, "name", scriptNames.DisplayName(f.Key))),
                characters = status.Characters.Select(c => Data(c.Counts, "speaker", c.Speaker)),
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteText(cli.Ansi, project, status, scriptNames);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteText(IAnsiConsole ansi, ProjectFolder project, ProjectStatus status, ScriptNames scriptNames)
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
        if (total.Keep > 0)
        {
            ansi.MarkupLine($"Aprovadas inclui {CliFormat.Count(total.Keep, "linha", "linhas")} em Não traduzir (ficam como no original).");
        }

        ansi.WriteLine();
        var named = status.Files.Any(file => scriptNames.DisplayName(file.Key) is not null);
        var scripts = named
            ? CliViews.NewTable(CliViews.Column("Script"), CliViews.Column("Nome"), CliViews.Column("Linhas", right: true), CliViews.Column("Progresso"), CliViews.Column("%", right: true))
            : CliViews.NewTable(CliViews.Column("Script"), CliViews.Column("Linhas", right: true), CliViews.Column("Progresso"), CliViews.Column("%", right: true));
        foreach (var file in status.Files)
        {
            var cells = new List<IRenderable> { new Markup(CliViews.Escape(file.Key)) };
            if (named)
            {
                cells.Add(new Markup(CliViews.Escape(scriptNames.DisplayName(file.Key) ?? string.Empty)));
            }

            cells.Add(new Markup(CliFormat.Number(file.Counts.Total)));
            cells.Add(DistributionBar.Create(file.Counts, RowBarWidth));
            cells.Add(new Markup(CliFormat.Percent(file.Counts.Percent)));
            scripts.AddRow(cells.ToArray());
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

    // scripts.json inválido não derruba o status: sem ele os scripts aparecem só pela chave.
    private static ScriptNames ScriptNamesOf(ProjectFolder project)
    {
        try
        {
            return project.LoadScriptNames();
        }
        catch (GlossaryFileException)
        {
            return ScriptNames.Empty;
        }
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

    private static Dictionary<string, object?> Data(StatusCounts counts, string? keyName = null, string? keyValue = null, string? extraName = null, string? extraValue = null)
    {
        var data = new Dictionary<string, object?>();
        if (keyName is not null)
        {
            data[keyName] = keyValue;
        }

        if (extraName is not null)
        {
            data[extraName] = extraValue;
        }

        data["lines"] = counts.Total;
        data["untranslated"] = counts.Untranslated;
        data["machine"] = counts.Machine;
        data["translated"] = counts.Translated;
        data["reviewed"] = counts.Reviewed;
        data["approved"] = counts.Approved;
        data["keep"] = counts.Keep;
        data["percent"] = counts.Percent;
        return data;
    }
}
