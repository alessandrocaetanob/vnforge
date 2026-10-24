using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da lista de recentes: um projeto legível ou um que não abriu (<see cref="Problem"/>).</summary>
public sealed class RecentProjectItem
{
    private RecentProjectItem(string path, string name, ProjectSummary? summary, string? problem, string whenLabel)
    {
        Path = path;
        Name = name;
        Summary = summary;
        Problem = problem;
        WhenLabel = whenLabel;
        Distribution = summary is null ? StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0)) : StatusDistribution.Of(summary.Counts);
    }

    public string Path { get; }

    public string Name { get; }

    public ProjectSummary? Summary { get; }

    /// <summary>Por que o projeto não abriu; nulo quando abriu.</summary>
    public string? Problem { get; }

    public bool IsAvailable => Summary is not null;

    public bool HasProblem => Problem is not null;

    public StatusDistribution Distribution { get; }

    public string WhenLabel { get; }

    public string EngineLabel => Summary is null ? string.Empty : ProjectLabels.Engine(Summary.EngineId, Summary.EngineVersion);

    /// <summary>Engine e caminho, a segunda linha da lista.</summary>
    public string Subtitle => Summary is null ? Path : $"{EngineLabel} · {Path}";

    public string PercentLabel => Summary is null ? "—" : $"{(int)Math.Floor(Summary.Counts.Percent)}%";

    public string Lines => Summary is null ? "—" : ProjectLabels.Number(Summary.Counts.Total);

    public string Scripts => Summary is null ? "—" : ProjectLabels.Number(Summary.Scripts);

    public string EncodingLabel => Summary is null ? "—" : ProjectLabels.Encoding(Summary.EncodingStrategy);

    public string ModelLabel => Summary is null ? "—" : ProjectLabels.Model(Summary.Model);

    /// <summary>Descrição da barra de distribuição para leitor de tela.</summary>
    public string DistributionDescription =>
        $"Aprovadas {Distribution.Approved}%, revisadas {Distribution.Reviewed}%, traduzidas {Distribution.Translated}%, IA {Distribution.Machine}%, pendentes {Distribution.Pending}%";

    public static RecentProjectItem FromSummary(ProjectSummary summary, DateTimeOffset openedAt, DateTimeOffset now) =>
        new(summary.Root, summary.Name, summary, problem: null, RelativeTime.Format(openedAt, now));

    public static RecentProjectItem FromProblem(string path, string problem, DateTimeOffset openedAt, DateTimeOffset now) =>
        new(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } folder ? folder : path, summary: null, problem, RelativeTime.Format(openedAt, now));
}
