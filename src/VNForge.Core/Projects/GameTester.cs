using VNForge.Core.Engines;
using VNForge.Core.Injection;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>O que o teste no jogo faz (<c>vnf test</c> e a tela "Testar no jogo").</summary>
/// <param name="OnlyScript">Chave da cena (<c>yst00012</c>) para testar só ela; as outras ficam como no jogo original. Não combina com <paramref name="AccentTest"/>.</param>
/// <param name="AccentTest">Injeta só <see cref="GameTester.AccentPhrase"/> na primeira fala do primeiro script, sem olhar a tradução do projeto.</param>
/// <param name="AllowWarnings">Segue mesmo com avisos da Conferência; erros travam sempre.</param>
/// <param name="OpenGame">Abre o executável no fim.</param>
public sealed record GameTestOptions(string? OnlyScript = null, bool AccentTest = false, bool AllowWarnings = false, bool OpenGame = true);

/// <summary>Os nomes dos passos em português, os mesmos na tela e na linha de comando.</summary>
public static class GameTestStepLabels
{
    public static string Label(this GameTestStep step) => step switch
    {
        GameTestStep.Check => "Conferir a tradução",
        GameTestStep.Build => "Preparar os arquivos",
        GameTestStep.Backup => "Guardar a cópia de segurança",
        GameTestStep.Inject => "Copiar para o jogo",
        GameTestStep.Open => "Abrir o jogo",
        _ => step.ToString(),
    };
}

/// <summary>Quanto um passo levou.</summary>
public sealed record GameTestStepResult(GameTestStep Step, TimeSpan Elapsed);

/// <param name="Build">O build que foi para o jogo.</param>
/// <param name="Injection">O que a injeção gravou; sem backup nem arquivos quando o jogo já estava igual ao build.</param>
/// <param name="Executable">O executável aberto; nulo quando <see cref="GameTestOptions.OpenGame"/> é falso.</param>
public sealed record GameTestResult(GameTestKind Kind, IReadOnlyList<GameTestStepResult> Steps, BuildResult Build, InjectionResult Injection, string? Executable);

/// <summary>
/// O teste no jogo com um clique: confere a tradução, reconstrói o <c>build/</c>, guarda a cópia de segurança, copia para a pasta do jogo e abre o
/// executável. A gravação no jogo é só a de <see cref="GameInjector"/>, que faz o backup antes de escrever (regra 2 do CLAUDE.md); aqui nada mais toca a
/// pasta do jogo. O CLI (<c>vnf test</c>) e o app usam este serviço, e o executável é aberto por <see cref="IGameProcessStarter"/>, que os testes trocam.
/// </summary>
public sealed class GameTester(
    IReadOnlyList<IEngine> engines,
    IGameProcessStarter starter,
    QaEngine? qa = null,
    TimeProvider? clock = null)
{
    /// <summary>A frase do teste de acentos: tem ç, ã, é e ô, os caracteres que mais dão problema na codificação do jogo.</summary>
    public const string AccentPhrase = "Teste de acentos: ação, coração, é, avô, não.";

    /// <summary>O nome que fala a frase dos acentos: o falante japonês da primeira fala não cabe na codificação do jogo traduzido.</summary>
    public const string AccentSpeaker = "Teste";

    private readonly IReadOnlyList<IEngine> _engines = engines ?? throw new ArgumentNullException(nameof(engines));
    private readonly IGameProcessStarter _starter = starter ?? throw new ArgumentNullException(nameof(starter));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// Roda os cinco passos. Nada é gravado no jogo antes de a Conferência passar e o build estar pronto, e o executável é procurado antes de qualquer gravação,
    /// para um jogo que não abre não ficar com a tradução injetada.
    /// </summary>
    /// <param name="steps">Recebe o passo que está começando.</param>
    /// <param name="scripts">Andamento do build, script a script.</param>
    /// <exception cref="QaBlockedException">A Conferência achou erro (ou aviso, sem <see cref="GameTestOptions.AllowWarnings"/>): nada foi gravado.</exception>
    /// <exception cref="VNForgeException">Opções que não combinam, cena ou fala inexistente, executável ausente, falha de build, de injeção ou ao abrir o jogo.</exception>
    public GameTestResult Run(ProjectFolder project, GameTestOptions? options = null, IProgress<GameTestStep>? steps = null, IProgress<ScriptWorkProgress>? scripts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new GameTestOptions();
        if (options.AccentTest && options.OnlyScript is not null)
        {
            throw new VNForgeException("O teste de acentos usa a primeira fala do primeiro script; não dá para combinar com a escolha de uma cena.");
        }

        var executable = options.OpenGame ? ResolveExecutable(project) : null;
        var kind = options.AccentTest ? GameTestKind.AccentTest : GameTestKind.Translation;
        var timeline = new StepTimeline(_clock, steps);

        timeline.Begin(GameTestStep.Check);
        var build = ProjectBuilder.Build(
            project,
            _engines,
            qa: qa,
            allowWarnings: options.AllowWarnings,
            options: BuildFor(options),
            progress: new ActionProgress<ScriptWorkProgress>(p =>
            {
                // O Core relata o primeiro script depois da Conferência e antes de reconstruir: ali termina um passo e começa o outro.
                timeline.Begin(GameTestStep.Build);
                scripts?.Report(p);
            }),
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        timeline.Begin(GameTestStep.Backup);
        var plan = GameInjector.Plan(project);
        var injection = new GameInjector(_clock).Inject(plan, new ActionProgress<InjectionPhase>(phase => timeline.Begin(phase == InjectionPhase.Backup ? GameTestStep.Backup : GameTestStep.Inject)));
        timeline.Begin(GameTestStep.Inject);

        // O jogo já tem o build: o estado é gravado antes de abrir o executável, para uma falha ao abrir não esconder o que está na pasta.
        GameStateFile.TryUpdate(project, saved =>
        {
            var backup = injection.BackupDirectory is null ? saved.InGame?.BackupDirectory : Path.GetFileName(injection.BackupDirectory);
            return saved with { InGame = new GameInGame(kind, _clock.GetUtcNow(), backup, options.OnlyScript, BuildState.Load(project)?.TextFingerprint) };
        });

        if (executable is not null)
        {
            timeline.Begin(GameTestStep.Open);
            new GameLauncher(_starter).Launch(project);
        }

        var results = timeline.Finish();
        var times = results.Select(r => new GameTestStepTime(r.Step, (long)r.Elapsed.TotalMilliseconds)).ToList();
        GameStateFile.TryUpdate(project, saved => saved with { LastTest = new GameLastTest(_clock.GetUtcNow(), kind, times) });
        return new GameTestResult(kind, results, build, injection, executable);
    }

    /// <summary>
    /// "Voltar ao jogo original": devolve os arquivos do backup e esquece o build que estava no jogo. A tradução continua no projeto.
    /// </summary>
    /// <exception cref="InjectionException">Não há backup para devolver, ou o jogo mudou depois da injeção.</exception>
    public RestoreResult ReturnToOriginal(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var result = new GameInjector(_clock).Restore(GameInjector.PlanRestore(project));
        GameStateFile.TryClearInGame(project);
        return result;
    }

    private static string ResolveExecutable(ProjectFolder project)
    {
        var executable = GameLauncher.ResolveExecutable(project);
        return File.Exists(executable) ? executable : throw new GameExecutableNotFoundException(executable);
    }

    private static BuildOptions BuildFor(GameTestOptions options)
    {
        if (options.AccentTest)
        {
            return new BuildOptions(SelectTexts: AccentOnly);
        }

        return options.OnlyScript is { } key
            ? new BuildOptions(SelectTexts: texts => OnlyScript(texts, key))
            : new BuildOptions(OnlyChanged: true);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> OnlyScript(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts, string key)
    {
        var found = texts.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        return found.Value is { } units
            ? new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal) { [found.Key] = units }
            : throw new VNForgeException($"A cena '{key}' não tem texto neste projeto. Confira o nome do script.");
    }

    /// <summary>
    /// A primeira fala do primeiro script com a frase dos acentos, dita por <see cref="AccentSpeaker"/> quando a fala tem falante; as outras linhas desse
    /// script voltam a ficar sem tradução, para só a frase ir para o jogo.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> AccentOnly(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts)
    {
        foreach (var (key, units) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var index = units.ToList().FindIndex(u => u.Kind == TextUnitKind.Dialogue);
            if (index < 0)
            {
                continue;
            }

            IReadOnlyList<TextUnit> only =
            [
                .. units.Select((u, i) => i == index
                    ? u with { Translation = AccentPhrase, Status = TranslationStatus.Translated, Speaker = u.Speaker is null ? null : AccentSpeaker }
                    : u with { Translation = null, Status = TranslationStatus.Untranslated }),
            ];
            return new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal) { [key] = only };
        }

        throw new VNForgeException("O projeto não tem nenhuma fala para o teste de acentos. Rode vnf extract e tente de novo.");
    }

    /// <summary>Marca quando cada passo começa; um passo termina quando o seguinte começa ou em <see cref="Finish"/>.</summary>
    private sealed class StepTimeline(TimeProvider clock, IProgress<GameTestStep>? report)
    {
        private readonly List<GameTestStepResult> _done = [];
        private GameTestStep? _current;
        private long _startedAt;

        /// <summary>Começa o passo; pedir de novo o passo que já corre, ou um anterior ao atual, não faz nada.</summary>
        public void Begin(GameTestStep step)
        {
            if (_current is { } running && step <= running)
            {
                return;
            }

            Close();
            _current = step;
            _startedAt = clock.GetTimestamp();
            report?.Report(step);
        }

        public IReadOnlyList<GameTestStepResult> Finish()
        {
            Close();
            _current = null;
            // Passo que não rodou (jogo não aberto) entra com zero, e a lista sempre sai na ordem dos passos.
            return [.. Enum.GetValues<GameTestStep>().Select(s => _done.FirstOrDefault(d => d.Step == s) ?? new GameTestStepResult(s, TimeSpan.Zero))];
        }

        private void Close()
        {
            if (_current is { } running)
            {
                _done.Add(new GameTestStepResult(running, clock.GetElapsedTime(_startedAt)));
            }
        }
    }

    private sealed class ActionProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
