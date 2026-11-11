using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using VNForge.Core.Logging;
using VNForge.Core.Output;
using VNForge.Core.Projects;
using VNForge.Core.Review;

namespace VNForge.Cli;

/// <summary><c>vnf status --risk</c>: a fila "o que mais precisa de você" do modo revisão, lida de <c>text/*.json</c>, do <c>.cache/sessions.json</c> e do <c>.cache/editor-suggestions.json</c>.</summary>
internal static class RiskStatusReport
{
    public const int DefaultTop = 10;

    private const int MinBarWidth = 10;
    private const int MaxBarWidth = 60;

    private static readonly (string Label, Spectre.Console.Color Color, char Glyph)[] Levels =
    [
        ("Alto", CliPalette.Error, '█'),
        ("Médio", CliPalette.Warning, '▒'),
        ("Baixo", CliPalette.Pending, '·'),
    ];

    public static int Run(VnfCli cli, ProjectFolder project, int top, bool json, TextWriter output)
    {
        var lines = ProjectLines.Read(project);
        var queue = RiskQueue.Build(lines, EditorSuggestionsFile.Load(project));
        var high = queue.Count(RiskLevel.High);
        var pace = ReviewPace.Compute(SessionsFile.Load(project), queue.Lines.Count, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        var shown = queue.Lines.Take(top).ToList();

        if (json)
        {
            output.WriteLine(JsonEnvelope.Result(new
            {
                lines = queue.Lines.Count,
                high,
                medium = queue.Count(RiskLevel.Medium),
                low = queue.Count(RiskLevel.Low),
                distribution = new { high = queue.Distribution.High, medium = queue.Distribution.Medium, low = queue.Distribution.Low },
                perHour = pace.PerHour,
                minutesToClearHigh = RiskQueue.TimeToReview(high, pace) is { } time ? (int?)time.TotalMinutes : null,
                top = shown.Select(Data),
            }));
            return ErrorHandler.OkExitCode;
        }

        WriteText(cli.Ansi, project, queue, high, pace, shown);
        return ErrorHandler.OkExitCode;
    }

    private static void WriteText(IAnsiConsole ansi, ProjectFolder project, RiskQueue queue, int high, ReviewPace pace, List<LineRisk> shown)
    {
        var accent = CliPalette.Hex(CliPalette.Accent);
        ansi.MarkupLine($"[bold {accent}]Fila por risco · {CliViews.Escape(project.LoadConfig().Name)}[/]");
        ansi.MarkupLine(queue.Lines.Count == 0
            ? "Nenhuma linha esperando revisão"
            : CliFormat.Count(queue.Lines.Count, "linha esperando revisão", "linhas esperando revisão"));
        if (queue.Lines.Count == 0)
        {
            return;
        }

        ansi.Write(Bar(queue, Math.Clamp(ansi.Profile.Width - 2, MinBarWidth, MaxBarWidth)));
        ansi.WriteLine();
        ansi.Write(Legend(queue.Distribution));
        ansi.WriteLine();
        ansi.MarkupLine(CliViews.Escape(RiskQueue.DescribeHigh(high, pace)));
        ansi.WriteLine();

        var names = StatusCommand.CharacterNames(project);
        var table = CliViews.NewTable(CliViews.Column("Risco", right: true), CliViews.Column("Nível"), CliViews.Column("Linha"), CliViews.Column("Falante"), CliViews.Column("Sinais"));
        foreach (var risk in shown)
        {
            var (label, color, _) = Levels[LevelIndex(risk.Level)];
            table.AddRow(
                new Markup(risk.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Markup($"[{CliPalette.Hex(color)}]{label}[/]"),
                new Markup(CliViews.Escape(risk.Line.Unit.Id)),
                new Markup(StatusCommand.SpeakerLabel(risk.Line.Unit.EffectiveSpeaker, names)),
                new Markup(CliViews.Escape(risk.Factors.Count == 0 ? "-" : string.Join(" · ", risk.Factors.Select(f => f.Label)))));
        }

        ansi.Write(table);
    }

    private static Markup Bar(RiskQueue queue, int width)
    {
        var cells = DistributionBar.Allocate([queue.Count(RiskLevel.High), queue.Count(RiskLevel.Medium), queue.Count(RiskLevel.Low)], width);
        var markup = new StringBuilder();
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] > 0)
            {
                markup.Append('[').Append(CliPalette.Hex(Levels[i].Color)).Append(']').Append(Levels[i].Glyph, cells[i]).Append("[/]");
            }
        }

        return new Markup(markup.ToString());
    }

    private static Markup Legend(RiskDistribution distribution)
    {
        int[] shares = [distribution.High, distribution.Medium, distribution.Low];
        return new Markup(string.Join("  ", Levels.Select((level, i) => $"[{CliPalette.Hex(level.Color)}]{level.Glyph}[/] {level.Label} {shares[i]}%")));
    }

    private static int LevelIndex(RiskLevel level) => level switch
    {
        RiskLevel.High => 0,
        RiskLevel.Medium => 1,
        _ => 2,
    };

    private static Dictionary<string, object?> Data(LineRisk risk) => new()
    {
        ["script"] = risk.Line.ScriptKey,
        ["id"] = risk.Line.Unit.Id,
        ["number"] = risk.Line.Number,
        ["speaker"] = risk.Line.Unit.EffectiveSpeaker,
        ["score"] = risk.Score,
        ["level"] = risk.Level switch { RiskLevel.High => "high", RiskLevel.Medium => "medium", _ => "low" },
        ["factors"] = risk.Factors.Select(f => new { kind = KindName(f.Kind), points = f.Points, label = f.Label }),
    };

    private static string KindName(RiskFactorKind kind) => kind switch
    {
        RiskFactorKind.AiDoubt => "ai-doubt",
        RiskFactorKind.QaIssue => "qa",
        RiskFactorKind.SoundsOdd => "sounds-odd",
        RiskFactorKind.EditorSuggestion => "editor-suggestion",
        _ => kind.ToString(),
    };
}
