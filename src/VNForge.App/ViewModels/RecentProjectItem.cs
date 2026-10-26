using VNForge.AI.Cost;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Engines.YuRis;

namespace VNForge.App.ViewModels;

/// <summary>Uma linha da lista de recentes: um projeto legível ou um que não abriu (<see cref="Problem"/>).</summary>
public sealed class RecentProjectItem
{
    private const string UnsupportedEngineConsequence = "Ele ainda não pode ser traduzido por aqui.";

    private readonly string lastBuildLabel;

    private RecentProjectItem(string path, string name, ProjectSummary? summary, string? problem, string whenLabel, string lastBuildLabel)
    {
        Path = path;
        Name = name;
        Summary = summary;
        Problem = problem;
        WhenLabel = whenLabel;
        this.lastBuildLabel = lastBuildLabel;
        Distribution = summary is null ? StatusDistribution.Of(new StatusCounts(0, 0, 0, 0, 0)) : StatusDistribution.Of(summary.Counts);
    }

    public string Path { get; }

    public string Name { get; }

    public ProjectSummary? Summary { get; }

    /// <summary>Por que o projeto não abriu; nulo quando abriu.</summary>
    public string? Problem { get; }

    public bool IsAvailable => Summary is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>A pasta abriu e o motor dela é um que o VNForge sabe traduzir.</summary>
    public bool IsSupported => Summary is not null && Summary.EngineId == YuRisEngine.EngineId;

    /// <summary>A pasta abriu, mas o motor ainda não tem suporte: o projeto aparece na lista e não abre.</summary>
    public bool IsUnsupported => Summary is not null && !IsSupported;

    /// <summary>Indisponível ou sem suporte: a linha troca a porcentagem pelo aviso, em cor de alerta.</summary>
    public bool HasStatusWarning => HasProblem || IsUnsupported;

    public StatusDistribution Distribution { get; }

    public string WhenLabel { get; }

    public string EngineLabel => Summary is null ? string.Empty : EngineText(Summary.EngineId, Summary.EngineVersion);

    /// <summary>Só o nome da engine, sem o formato do pacote: "KiriKiri/KAG".</summary>
    private string EngineFamily => Summary?.EngineId switch
    {
        "kirikiri" => "KiriKiri/KAG",
        _ => EngineLabel,
    };

    /// <summary>A engine como rótulo do detalhe; a v3 escreve os rótulos em caixa normal.</summary>
    public string EngineOverline => EngineLabel;

    /// <summary>Engine e caminho, a segunda linha da lista.</summary>
    public string Subtitle => Summary is null ? Path : $"{EngineLabel} · {Path}";

    public string PercentLabel => Summary is null ? "—" : $"{(int)Math.Floor(Summary.Counts.Percent)}%";

    /// <summary>A coluna "Traduzido" da lista: a porcentagem, ou o aviso quando o projeto não abre.</summary>
    public string RowStatusLabel => HasProblem ? "Indisponível" : IsUnsupported ? "Sem suporte" : PercentLabel;

    /// <summary>O que o aviso de motor sem suporte diz ao usuário.</summary>
    public string UnsupportedDetail => $"A pasta foi reconhecida como {EngineFamily}. {UnsupportedEngineConsequence}";

    public string Lines => Summary is null ? "—" : ProjectLabels.Number(Summary.Counts.Total);

    /// <summary>Linhas prontas (revisadas ou aprovadas), esperando revisão e total: os números do anel e da barra de três estados, os mesmos do Painel.</summary>
    public double ReadyLines => Summary?.Counts.Ready ?? 0;

    public double ReviewLines => Summary?.Counts.ToReview ?? 0;

    public double TotalLines => Summary?.Counts.Total ?? 0;

    /// <summary>A parte pronta como a lista e o anel a escrevem, arredondada para baixo: "42%". "—" quando o projeto não abriu.</summary>
    public string ReadyLabel => Summary is null ? "—" : $"{ReadyPercent(Summary.Counts)}%";

    /// <summary>"48.211 linhas", a segunda linha da lista.</summary>
    public string LinesLabel => Summary is null ? string.Empty : LinesText(Summary.Counts.Total);

    /// <summary>A coluna "Pronto" da lista: a parte pronta, ou o aviso quando o projeto não abre.</summary>
    public string ReadyColumnLabel => ReadyColumnText();

    /// <summary>Descrição do anel e da barra para leitor de tela.</summary>
    public string ReadyDescription => $"{ReadyLabel} pronto: {ProjectLabels.Number((int)ReadyLines)} de {LinesLabel}";

    /// <summary>"Rápida", "Equilibrada" ou "Caprichada" nos detalhes técnicos; o nome do modelo quando ele não é de nenhuma qualidade.</summary>
    public string QualityLabel => Summary is null ? "—" : QualityText(Summary.Model);

    private string ReadyColumnText()
    {
        if (HasProblem)
        {
            return "Indisponível";
        }

        return IsUnsupported ? "Sem suporte" : ReadyLabel;
    }

    private static int ReadyPercent(StatusCounts counts) =>
        counts.Total == 0 ? 0 : (int)Math.Floor(100.0 * counts.Ready / counts.Total);

    private static string LinesText(int total) => total == 1 ? "1 linha" : $"{ProjectLabels.Number(total)} linhas";

    private static string QualityText(string model) =>
        QualityProfiles.ForModel(model) is { } profile ? QualityOption.Describe(profile).Title : ProjectLabels.Model(model);

    public string Scripts => Summary is null ? "—" : ProjectLabels.Number(Summary.Scripts);

    public string EncodingLabel => Summary is null ? "—" : ProjectLabels.Encoding(Summary.EncodingStrategy);

    /// <summary>Modelo e esforço ("Claude Opus 5.5 · esforço médio"); o Haiku não leva esforço, como no artboard.</summary>
    public string ModelLabel => Summary is null ? "—" : ModelText(Summary);

    /// <summary>Quando foi o último build e quantos scripts ele trocou; "Nenhum build ainda" quando <c>build/</c> está vazia.</summary>
    public string LastBuildLabel => lastBuildLabel;

    /// <summary>O script do botão "Continuar em": o último editado; nulo quando o projeto ainda não tem scripts.</summary>
    public string? ResumeScript => Summary?.LastEditedScript;

    public string ResumeLabel => ResumeScript is { } script ? $"Continuar em {script}" : "Continuar no Editor";

    /// <summary>Descrição da barra de distribuição para leitor de tela.</summary>
    public string DistributionDescription =>
        $"Aprovadas {Distribution.Approved}%, revisadas {Distribution.Reviewed}%, traduzidas {Distribution.Translated}%, IA {Distribution.Machine}%, pendentes {Distribution.Pending}%";

    public static RecentProjectItem FromSummary(ProjectSummary summary, DateTimeOffset openedAt, DateTimeOffset now) =>
        new(summary.Root, summary.Name, summary, problem: null, RelativeTime.Format(openedAt, now), LastBuildText(summary.LastBuild, now));

    public static RecentProjectItem FromProblem(string path, string problem, DateTimeOffset openedAt, DateTimeOffset now) =>
        new(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } folder ? folder : path, summary: null, problem, RelativeTime.Format(openedAt, now), "—");

    private static string EngineText(string id, int? version) => id switch
    {
        // A família vem da versão do YPF: v500 é YU-RIS 5.x, como o artboard e a detecção do `vnf detect` escrevem.
        YuRisEngine.EngineId => version is { } v ? $"YU-RIS {v / 100}.x · YPF v{v}" : "YU-RIS",
        "kirikiri" => "KiriKiri/KAG · XP3",
        _ => ProjectLabels.Engine(id, version),
    };

    private static string ModelText(ProjectSummary summary)
    {
        var model = ProjectLabels.Model(summary.Model);
        var effort = summary.Effort switch
        {
            "low" => "baixo",
            "medium" => "médio",
            "high" => "alto",
            _ => null,
        };
        return effort is null || summary.Model.Contains("haiku", StringComparison.OrdinalIgnoreCase) ? model : $"{model} · esforço {effort}";
    }

    private static string LastBuildText(LastBuild? build, DateTimeOffset now)
    {
        if (build is null)
        {
            return "Nenhum build ainda";
        }

        var when = RelativeTime.Format(build.BuiltAt, now);
        return build.Scripts switch
        {
            null => when,
            0 => $"{when} · ida e volta sem tradução: arquivo idêntico",
            1 => $"{when} · 1 script",
            { } n => $"{when} · {ProjectLabels.Number(n)} scripts",
        };
    }
}
