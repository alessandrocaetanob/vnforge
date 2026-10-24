using VNForge.Core.Injection;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Etapas do pipeline que o Painel mostra, na ordem do fluxo.</summary>
public enum PipelineStageKey
{
    Extract,
    Translate,
    Review,
    Build,
    Inject,
}

/// <summary>Uma etapa: <see cref="IsComplete"/> quando nada falta nela; <see cref="Pending"/> é quanto falta (linhas, ou 1 para uma etapa de tudo ou nada).</summary>
public sealed record PipelineStage(PipelineStageKey Key, bool IsComplete, int Pending);

/// <summary>Tudo que o Painel do projeto mostra. O progresso é o mesmo <see cref="ProjectStatus"/> que o <c>vnf status</c> imprime.</summary>
public sealed record ProjectDashboard(ProjectStatus Status, IReadOnlyList<PipelineStage> Stages, AiUsageTotals AiUsage)
{
    /// <summary>Etapa <see cref="PipelineStageKey.Review"/> (Conferir): linhas que a IA ou o tradutor deixaram e ninguém revisou.</summary>
    public static int AwaitingReview(StatusCounts counts) => counts.Machine + counts.Translated;

    /// <exception cref="TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    /// <exception cref="VNForgeException"><c>.cache/state.json</c> ilegível.</exception>
    public static ProjectDashboard Read(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var texts = project.LoadAllText();
        var status = ProjectStatusReporter.Create(texts);
        var counts = status.Total;

        var extracted = project.LoadState().Stages.ContainsKey(ProjectExtractor.ExtractStage);
        var awaiting = AwaitingReview(counts);
        var built = IsBuildCurrent(project, texts);
        var injected = built && IsInjected(project);

        PipelineStage[] stages =
        [
            new(PipelineStageKey.Extract, extracted, extracted ? 0 : 1),
            new(PipelineStageKey.Translate, counts.Total > 0 && counts.Untranslated == 0, counts.Untranslated),
            new(PipelineStageKey.Review, counts.Total > 0 && awaiting == 0 && counts.Untranslated == 0, awaiting),
            new(PipelineStageKey.Build, built, built ? 0 : 1),
            new(PipelineStageKey.Inject, injected, injected ? 0 : 1),
        ];
        return new ProjectDashboard(status, stages, AiUsageLog.Load(project));
    }

    /// <summary>
    /// O último build está íntegro (<see cref="BuildState.ArchivesIntact"/>) e foi feito com os mesmos textos, formatação e estratégia de hoje
    /// (<see cref="BuildState.FingerprintTexts"/>). Um build sem impressão digital (de uma versão anterior) conta como desatualizado até o próximo build.
    /// </summary>
    private static bool IsBuildCurrent(ProjectFolder project, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        if (BuildState.Load(project) is not { TextFingerprint: { } saved } build || !build.ArchivesIntact(project))
        {
            return false;
        }

        var strategy = project.LoadConfig().Encoding.Strategy;
        return string.Equals(saved, BuildState.FingerprintTexts(texts, strategy, project.FormattingPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// A injeção vigente corresponde ao <c>build/</c>: o backup mais recente existe e cada arquivo que ele injetou está no jogo com o tamanho do arquivo
    /// do <c>build/</c>. Comparar tamanho é barato (hashear os pacotes do jogo a cada abertura do Painel não é); um <c>vnf restore</c> devolve os originais,
    /// que têm outro tamanho, e a etapa volta a pendente. Sem backup legível, ou com o jogo fora do lugar, não conta como injetado.
    /// </summary>
    private static bool IsInjected(ProjectFolder project)
    {
        try
        {
            var plan = GameInjector.PlanRestore(project);
            return plan.Manifest.Files.Count > 0 && plan.Manifest.Files.All(entry =>
            {
                var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                var built = new FileInfo(Path.Combine(project.BuildDirectory, relative));
                var injected = new FileInfo(Path.Combine(plan.GameDirectory, relative));
                return built.Exists && injected.Exists && built.Length == injected.Length;
            });
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
