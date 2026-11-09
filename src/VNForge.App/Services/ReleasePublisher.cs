using VNForge.Core;
using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Core.Qa;

namespace VNForge.App.Services;

/// <summary>Se o jogo da pasta é o da versão de referência do projeto, o que o <c>vnf release</c> só descobre ao gerar.</summary>
public enum ReleaseReference
{
    /// <summary>Sem <c>build/</c> não há como conferir antes; a conferência acontece ao gerar.</summary>
    Unknown,
    Matches,
    Differs,
}

/// <summary>O que a tela Lançar mostra antes de gerar: a Conferência, o último teste, a versão anterior e o que mudou desde ela.</summary>
/// <param name="PreviousVersion">A versão do patch anterior; nula na primeira.</param>
/// <param name="PreviousAt">Quando a versão anterior foi gerada.</param>
/// <param name="Changes">As cenas cuja tradução mudou desde a versão anterior; <see cref="ChangesSinceTest.None"/> na primeira.</param>
/// <param name="ReferenceProblem">A mensagem do Core quando <paramref name="Reference"/> é <see cref="ReleaseReference.Differs"/>.</param>
/// <param name="PreviousFiles">Os arquivos do patch anterior, de onde vêm os tamanhos estimados de "O que o jogador recebe"; nulo na primeira versão.</param>
/// <param name="PreviousGameFiles">Os arquivos do jogo que o patch anterior tocou, a estimativa de "O que o patch muda no jogo"; nulo na primeira versão e nas anteriores a este registro.</param>
/// <param name="GameFileCount">Quantos arquivos a pasta do jogo tem; nulo quando ela não lê.</param>
/// <param name="ChangeExamples">Uma fala mudada por cena, com o antes e o depois; vazia quando o texto da versão anterior não foi guardado.</param>
/// <param name="ChangedFiles">Os arquivos do jogo cujo checksum não é o da extração, com o esperado e o encontrado; nulo fora de <see cref="ReleaseReference.Differs"/>.</param>
public sealed record ReleaseReadiness(
    string GameFolder,
    string? PreviousVersion,
    DateTimeOffset? PreviousAt,
    int QaErrors,
    int QaWarnings,
    DateTimeOffset? TestedAt,
    ReleaseReference Reference,
    string? ReferenceProblem,
    ChangesSinceTest Changes,
    IReadOnlyList<ReleaseFile>? PreviousFiles = null,
    IReadOnlyList<ReleaseGameFile>? PreviousGameFiles = null,
    int? GameFileCount = null,
    IReadOnlyList<ChangeExample>? ChangeExamples = null,
    IReadOnlyList<ChangedGameFile>? ChangedFiles = null);

/// <summary>O que a tela Lançar pede ao Core. Atrás de uma interface para os testes não gerarem patch de verdade.</summary>
public interface IReleasePublisher
{
    /// <summary>Lê o projeto; pesado (roda a Conferência e confere o jogo), então a tela chama fora da thread da interface.</summary>
    ReleaseReadiness Inspect(string projectRoot, CancellationToken cancellationToken);

    string DefaultInstallerName(string projectRoot, string version);

    /// <exception cref="QaBlockedException">A Conferência achou erro.</exception>
    /// <exception cref="InjectionException">O jogo não é a versão de referência.</exception>
    /// <exception cref="VNForgeException">Versão inválida, instalador ausente ou verificação do patch falhou.</exception>
    ReleaseResult Create(string projectRoot, ReleaseOptions options, IProgress<ReleaseProgress>? steps, CancellationToken cancellationToken);
}

/// <summary>O <see cref="ReleaseService"/> do <c>vnf release</c>, o mesmo que a CLI usa.</summary>
public sealed class ReleasePublisher(IReadOnlyList<IEngine> engines, QaEngine? qa = null, TimeProvider? clock = null) : IReleasePublisher
{
    /// <summary>Quantas falas de exemplo "Mudou desde o patch" mostra: uma por cena, nas primeiras cenas.</summary>
    public const int ExampleCount = 3;

    public ReleaseReadiness Inspect(string projectRoot, CancellationToken cancellationToken)
    {
        var project = ProjectFolder.Open(projectRoot);
        var config = project.LoadConfig();
        var gameFolder = Path.GetFullPath(config.Game.Path, project.Root);
        var report = (qa ?? QaEngine.Default).Run(QaContext.FromProject(project));
        cancellationToken.ThrowIfCancellationRequested();

        var tested = GameStateFile.Load(project).LastTest is { Kind: GameTestKind.Translation } last ? last.At : (DateTimeOffset?)null;
        var previous = ReleaseService.LoadRecords(project).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        var changes = ChangesSinceTest.None;
        IReadOnlyList<ChangeExample> examples = [];
        if (previous is not null)
        {
            try
            {
                var texts = project.LoadAllText();
                changes = TranslationChanges.Since(new GameLastTest(previous.CreatedAt, GameTestKind.Translation, [], previous.Snapshot), texts);
                examples = ReleaseTexts.Examples(ReleaseTexts.Load(project, previous.Version), texts, changes, ExampleCount);
            }
            catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
            {
                // A lista de mudanças é um complemento: sem textos legíveis a página abre como na primeira versão.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (reference, problem, changedFiles) = CheckReference(project);
        return new ReleaseReadiness(
            gameFolder,
            previous?.Version,
            previous?.CreatedAt,
            report.Errors.Count,
            report.BlockingIssues.Count(i => i.Severity == QaSeverity.Warning),
            tested,
            reference,
            problem,
            changes,
            previous?.Files,
            previous?.GameFiles,
            CountFiles(gameFolder),
            examples,
            changedFiles);
    }

    private static int? CountFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string DefaultInstallerName(string projectRoot, string version) =>
        ReleaseService.DefaultInstallerName(ProjectFolder.Open(projectRoot).LoadConfig(), version);

    public ReleaseResult Create(string projectRoot, ReleaseOptions options, IProgress<ReleaseProgress>? steps, CancellationToken cancellationToken) =>
        new ReleaseService(engines, qa, clock).Create(ProjectFolder.Open(projectRoot), options, steps, cancellationToken);

    private static (ReleaseReference Reference, string? Problem, IReadOnlyList<ChangedGameFile>? Changed) CheckReference(ProjectFolder project)
    {
        if (!Directory.Exists(project.BuildDirectory) || !Directory.EnumerateFileSystemEntries(project.BuildDirectory).Any())
        {
            return (ReleaseReference.Unknown, null, null);
        }

        try
        {
            GameInjector.Plan(project);
            return (ReleaseReference.Matches, null, null);
        }
        catch (InjectionException e)
        {
            return (ReleaseReference.Differs, e.Message, e.ChangedFiles);
        }
        catch (Exception e) when (e is VNForgeException or IOException or UnauthorizedAccessException)
        {
            return (ReleaseReference.Unknown, null, null);
        }
    }
}
