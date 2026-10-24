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

    /// <exception cref="VNForge.Core.Text.TextFileException">Algum <c>text/*.json</c> inválido.</exception>
    /// <exception cref="VNForgeException"><c>.cache/state.json</c> ilegível.</exception>
    public static ProjectDashboard Read(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var status = ProjectStatusReporter.Create(project);
        var counts = status.Total;

        var extracted = project.LoadState().Stages.ContainsKey(ProjectExtractor.ExtractStage);
        var awaiting = AwaitingReview(counts);
        var built = IsBuildCurrent(project);
        var injected = Directory.Exists(project.BackupDirectory) && Directory.EnumerateDirectories(project.BackupDirectory).Any();

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

    /// <summary>Há um build íntegro (<see cref="BuildState.ArchivesIntact"/>) e nenhum <c>text/*.json</c> foi gravado depois dele.</summary>
    private static bool IsBuildCurrent(ProjectFolder project)
    {
        if (BuildState.Load(project) is not { } build || !build.ArchivesIntact(project))
        {
            return false;
        }

        var builtAt = File.GetLastWriteTimeUtc(Path.Combine(project.CacheDirectory, BuildState.StateFileName));
        return !Directory.Exists(project.TextDirectory)
            || Directory.EnumerateFiles(project.TextDirectory, "*.json").All(f => File.GetLastWriteTimeUtc(f) <= builtAt);
    }
}
