using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Injection;
using VNForge.Core.Qa;
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

/// <summary>Situação do <c>build/</c>: sem build ainda, feito com textos que mudaram depois, ou em dia.</summary>
public enum BuildStatus
{
    None,
    Stale,
    Current,
}

/// <summary>O que a conferência acha no projeto: erros e avisos de QA, sem contar as linhas ainda sem tradução (o build copia o original nelas).</summary>
public sealed record QaTotals(int Errors, int Warnings)
{
    public int Total => Errors + Warnings;

    /// <summary>Avisos de estilo (honoríficos e gênero): aparecem na Conferência, mas não entram em <see cref="Warnings"/> nem em <see cref="Total"/>, porque não pedem revisão antes do build.</summary>
    public int StyleAdvice { get; init; }
}

/// <summary>Tudo que o Painel do projeto mostra. O progresso é o mesmo <see cref="ProjectStatus"/> que o <c>vnf status</c> imprime.</summary>
public sealed record ProjectDashboard(ProjectStatus Status, IReadOnlyList<PipelineStage> Stages, AiUsageTotals AiUsage)
{
    /// <summary>Erros e avisos de QA (as mesmas regras do <c>vnf check</c>); nulo quando o QA não pôde rodar (arquivo do projeto inválido, estratégia desconhecida).</summary>
    public QaTotals? Qa { get; init; }

    public BuildStatus Build { get; init; }

    /// <summary>Nomes legíveis dos scripts (<c>scripts.json</c>); vazio quando o arquivo não existe ou está inválido.</summary>
    public ScriptNames ScriptNames { get; init; } = ScriptNames.Empty;

    /// <summary>Tradução do nome de cada falante (<c>characters.json</c>, pelo nome original sem espaços nas pontas); vazio quando o arquivo não existe ou está inválido.</summary>
    public IReadOnlyDictionary<string, string> CharacterNames { get; init; } = new Dictionary<string, string>();

    /// <summary>Quando foi feito o backup mais recente que ainda não foi restaurado; nulo sem backup legível.</summary>
    public DateTimeOffset? LastBackupAt { get; init; }

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
        var restore = TryPlanRestore(project);
        var injected = built && IsInjected(project, restore);

        PipelineStage[] stages =
        [
            new(PipelineStageKey.Extract, extracted, extracted ? 0 : 1),
            new(PipelineStageKey.Translate, counts.Total > 0 && counts.Untranslated == 0, counts.Untranslated),
            new(PipelineStageKey.Review, counts.Total > 0 && awaiting == 0 && counts.Untranslated == 0, awaiting),
            new(PipelineStageKey.Build, built, built ? 0 : 1),
            new(PipelineStageKey.Inject, injected, injected ? 0 : 1),
        ];
        return new ProjectDashboard(status, stages, AiUsageLog.Load(project))
        {
            Qa = TryQa(project, texts),
            ScriptNames = TryLoad(project.LoadScriptNames, ScriptNames.Empty),
            CharacterNames = TryLoad(project.LoadCharacters, []).GroupBy(c => c.Source.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal),
            Build = built ? BuildStatus.Current : BuildState.Load(project) is null ? BuildStatus.None : BuildStatus.Stale,
            LastBackupAt = restore?.Manifest.CreatedAt,
        };
    }

    // Um arquivo de apoio inválido (nomes, personagens) não derruba o Painel: os números dependem só de text/*.json.
    private static T TryLoad<T>(Func<T> load, T fallback)
    {
        try
        {
            return load();
        }
        catch (GlossaryFileException)
        {
            return fallback;
        }
    }

    /// <summary>Roda o QA sobre os textos já lidos. As linhas sem tradução não entram: já aparecem como pendentes na etapa Traduzir.</summary>
    private static QaTotals? TryQa(ProjectFolder project, IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        try
        {
            var config = project.LoadConfig();
            var encoder = TextEncoderRegistry.Default.Create(config.Encoding.Strategy);
            var context = new QaContext(texts, project.LoadGlossary(), project.LoadCharacters(), project.LoadFormatting(), encoder) { Style = config.Style };
            var report = QaEngine.Default.Run(context);
            var issues = report.BlockingIssues;
            return new QaTotals(issues.Count(i => i.Severity == QaSeverity.Error), issues.Count(i => i.Severity == QaSeverity.Warning)) { StyleAdvice = report.StyleAdviceCount };
        }
        catch (VNForgeException)
        {
            return null;
        }
    }

    private static RestorePlan? TryPlanRestore(ProjectFolder project)
    {
        try
        {
            return GameInjector.PlanRestore(project);
        }
        catch (Exception ex) when (ex is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
    private static bool IsInjected(ProjectFolder project, RestorePlan? plan)
    {
        if (plan is null)
        {
            return false;
        }

        return plan.Manifest.Files.Count > 0 && plan.Manifest.Files.All(entry =>
        {
            var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
            var built = new FileInfo(Path.Combine(project.BuildDirectory, relative));
            var injected = new FileInfo(Path.Combine(plan.GameDirectory, relative));
            return built.Exists && injected.Exists && built.Length == injected.Length;
        });
    }
}
